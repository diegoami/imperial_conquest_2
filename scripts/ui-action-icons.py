"""T284: scoped Gemini UI artwork using the established authored-assets adapter.

No network in conformance/self-checks. No existing map/battle assets are rewritten.
"""
from pathlib import Path
import hashlib
import io
import json
import re
import contextlib
import tempfile
from types import SimpleNamespace
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
PROMPTS = ROOT / "assets" / "ui-action-prompts.json"
PREFIX = "ui.action."


def entries():
    document = json.loads(PROMPTS.read_text(encoding="utf-8"))
    names = [icon["name"] for icon in document["icons"]]
    if len(set(names)) != len(names) or any(not re.fullmatch(r"[a-z][a-z_]{0,40}", name) for name in names):
        raise ValueError("UI icon names must be unique plain identifiers")
    return document["style"], document["icons"]


def conform(image):
    image = image.convert("RGBA")
    pixels = list(image.getdata())
    transparent = sum(a < 16 for r, g, b, a in pixels) > len(pixels) // 20
    keyed = [min(r, b) - g > 35 and min(r, b) > 110 for r, g, b, a in pixels]
    if not transparent and sum(keyed) < len(pixels) // 10:
        raise ValueError("Expected transparency or a flat magenta background; raw image retained")
    # This palette deliberately excludes magenta. Clear it before resampling so
    # colored edges never blend the temporary matte into the finished icon.
    image.putdata([(0, 0, 0, 0) if key else (r, g, b, a)
                   for (r, g, b, a), key in zip(pixels, keyed)])
    # Generated sources occasionally contain detached corner flourishes. Keep
    # substantial components (including separate Pause bars / split shields),
    # rather than shrinking the real symbol to accommodate isolated specks.
    alpha = image.getchannel("A").tobytes()
    visited = bytearray(len(alpha)); groups = []
    width = image.width
    for start, value in enumerate(alpha):
        if value < 16 or visited[start]: continue
        stack = [start]; visited[start] = 1; group = []
        while stack:
            index = stack.pop(); group.append(index)
            x = index % width
            neighbors = []
            if x: neighbors.append(index - 1)
            if x + 1 < width: neighbors.append(index + 1)
            if index >= width: neighbors.append(index - width)
            if index + width < len(alpha): neighbors.append(index + width)
            for neighbor in neighbors:
                if not visited[neighbor] and alpha[neighbor] >= 16:
                    visited[neighbor] = 1; stack.append(neighbor)
        groups.append(group)
    if not groups: raise ValueError("Empty icon after matte removal")
    minimum = max(16, max(map(len, groups)) // 50)
    keep = bytearray(len(alpha))
    for group in groups:
        if len(group) >= minimum:
            for index in group: keep[index] = alpha[index]
    image.putalpha(Image.frombytes("L", image.size, bytes(keep)))
    box = image.getchannel("A").getbbox()
    if box is None:
        raise ValueError("Empty icon after matte removal")
    subject = image.crop(box)
    subject.thumbnail((28, 28), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (32, 32))
    canvas.alpha_composite(subject, ((32 - subject.width) // 2, (32 - subject.height) // 2))
    # Crisp small-icon alpha; PNG preserves it consistently across image readers.
    canvas.putdata([(r, g, b, 255) if a >= 128 else (0, 0, 0, 0)
                    for r, g, b, a in canvas.getdata()])
    if canvas.getbbox() is None:
        raise ValueError("Icon disappeared at toolbar size")
    return canvas


def safe_error(error):
    # Provider error bodies may contain request details. Never echo them or keys.
    match = re.search(r"HTTP (\d{3})", str(error))
    return "image provider HTTP " + match[1] if match else type(error).__name__


def self_check():
    errors = []
    source = Image.new("RGBA", (128, 128), (255, 0, 255, 255))
    draw = ImageDraw.Draw(source)
    draw.rectangle((36, 24, 56, 104), fill=(205, 145, 48))
    draw.rectangle((72, 24, 92, 104), fill=(45, 85, 135))
    draw.point((2, 2), fill=(0, 0, 0))
    result = conform(source)
    if result.size != (32, 32) or result.getpixel((0, 0))[3] != 0:
        errors.append("UI icon dimensions/transparency")
    colors = {p[:3] for p in result.getdata() if p[3]}
    if len(colors) < 2 or not any(b > r for r, g, b in colors):
        errors.append("UI illustration colors were lost")
    try:
        conform(Image.new("RGB", (32, 32), "white"))
        errors.append("unkeyed opaque image was accepted")
    except ValueError:
        pass
    return errors


def route_check():
    """Exercise real scoped routes with stubbed network and fresh scratch output."""
    errors = []
    with tempfile.TemporaryDirectory(prefix="ui-icon-routes-", dir=ROOT / "rendered") as scratch:
        scratch = Path(scratch).resolve()
        assert scratch.is_relative_to((ROOT / "rendered").resolve())
        out = scratch / "pack"; out.mkdir()
        (out / "keep.bmp").write_bytes(b"unrelated asset remains intact")
        (out / "manifest.json").write_text(json.dumps({"assets": {"keep": "keep.bmp"}}), encoding="utf-8")
        calls = []
        model = "google/gemini-test-image"
        secret = "ui-route-check-secret-never-print"
        def fake_image(config, prompt):
            calls.append(prompt)
            source = Image.new("RGB", (128, 128), (255, 0, 255))
            ImageDraw.Draw(source).rectangle((30, 20, 98, 108), fill=(55, 95, 145))
            buffer = io.BytesIO(); source.save(buffer, format="PNG")
            return buffer.getvalue(), {"returned_model": model, "cost": 0}
        adapter = SimpleNamespace(
            load_config=lambda _: {"endpoint": "https://openrouter.ai/api/v1", "model": model, "api_key": secret},
            fetch_models=lambda _: [{"id": model, "architecture": {"output_modalities": ["image"]}, "pricing": {}}],
            per_image_estimate=lambda _: 0, call_image_model=fake_image)
        args = SimpleNamespace(all=False, strategic_gemini=False, strategic_route_check=False,
            key=["ui.action.pause"], dry_run=True, reconform=False, config="unused", raw=str(scratch / "raw"), out=str(out))
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            run(args, adapter)
            if calls or (out / "ui").exists(): errors.append("dry run wrote/generated UI art")
            for field, value in [("all", True), ("key", ["army.tier1.icon"])]:
                original = getattr(args, field); setattr(args, field, value)
                try:
                    run(args, adapter); errors.append("unscoped UI generation was accepted")
                except ValueError:
                    pass
                finally:
                    setattr(args, field, original)
            args.dry_run = False
            run(args, adapter)
            if len(calls) != 1: errors.append("explicit key made the wrong request count")
            try:
                run(args, adapter); errors.append("existing paid UI art was overwritten")
            except ValueError:
                pass
            def forbidden(*_): raise AssertionError("offline route attempted config/network")
            args.reconform = True
            run(args, SimpleNamespace(load_config=forbidden, fetch_models=forbidden, call_image_model=forbidden))
        if secret in output.getvalue(): errors.append("credential appeared in route output")
        if (out / "keep.bmp").read_bytes() != b"unrelated asset remains intact": errors.append("unrelated asset changed")
        manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
        if set(manifest["assets"]) != {"keep", "ui.action.pause"}: errors.append("manifest wrote unrelated keys")
    return errors


def run(args, adapter):
    if args.all or args.strategic_gemini or args.strategic_route_check:
        raise ValueError("UI icon mode requires explicit UI keys; --all/strategic routes are not allowed")
    style, icons = entries()
    by_key = {PREFIX + icon["name"]: icon for icon in icons}
    keys = list(dict.fromkeys(args.key)) or list(by_key)
    if any(key not in by_key for key in keys):
        raise ValueError("Unknown UI icon key; valid keys: " + ", ".join(by_key))
    failures = self_check()
    if failures:
        raise ValueError("UI conformance self-check failed: " + ", ".join(failures))
    write = bool(args.key) and not args.dry_run
    raw_root = Path(args.raw) / "ui-actions"
    out_root = Path(args.out)
    if args.reconform:
        config = None  # Offline means no credentials, models lookup or request.
    else:
        config = adapter.load_config(args.config)
        if config["endpoint"].rstrip("/") != "https://openrouter.ai/api/v1":
            raise ValueError("UI artwork requires the requested OpenRouter endpoint")
        if not config["model"].startswith("google/gemini"):
            raise ValueError("UI artwork requires the requested Google Gemini image model; no fallback")
        models = adapter.fetch_models(config["endpoint"])
        model = next((m for m in models if m["id"] == config["model"]), None)
        if not model or "image" not in model.get("architecture", {}).get("output_modalities", []):
            raise ValueError("Configured Gemini model is not advertised as image-capable")
        print("Gemini UI model:", config["model"])
        print("Provider pricing:", json.dumps(model.get("pricing", {}), sort_keys=True))
        print("Selected images:", len(keys), "; requests now:", len(keys) if write else 0, "(no automatic retries/fallback)")
        estimate = adapter.per_image_estimate(model.get("pricing", {}))
        if estimate is not None:
            print(f"Rough selected-image estimate: ${estimate * len(keys):.4f}; actual usage recorded per response")
        if write and not config["api_key"]:
            raise ValueError("No configured OpenRouter credential; no request made")
    if not write:
        for key in keys:
            print("Would " + ("reconform " if args.reconform else "generate ") + key + ": " + by_key[key]["subject"])
        return 0
    raw_root.mkdir(parents=True, exist_ok=True)
    results = []
    for key in keys:
        icon = by_key[key]
        raw = raw_root / (icon["name"] + ".png")
        metadata = raw.with_suffix(".json")
        destination = out_root / "ui" / "actions" / (icon["name"] + ".png")
        prompt = style + "\nSubject: " + icon["subject"]
        if args.reconform:
            if not raw.exists() or not metadata.exists():
                raise ValueError("Missing raw/provenance for " + key)
            provenance = json.loads(metadata.read_text(encoding="utf-8"))
            if not provenance["requestedModel"].startswith("google/gemini") or provenance["returnedModel"] != provenance["requestedModel"]:
                raise ValueError("Raw provenance does not certify the requested Gemini model for " + key)
            data = raw.read_bytes()
            if hashlib.sha256(data).hexdigest() != provenance["rawSha256"]:
                raise ValueError("Raw/provenance hash mismatch for " + key)
        else:
            if raw.exists() or destination.exists():
                raise ValueError("Existing UI artwork is preserved; use --reconform offline for " + key)
            try:
                data, usage = adapter.call_image_model(dict(config, image_provenance=True), prompt)
            except Exception as error:
                print("FAILED", key, safe_error(error), "(no further requests made)")
                return 1
            # Preserve the paid original before decoding or attempting conformance.
            raw.write_bytes(data)
            provenance = {"key": key, "requestedModel": config["model"],
                "returnedModel": usage.get("returned_model"), "prompt": prompt,
                "rawSha256": hashlib.sha256(data).hexdigest(), "conformanceVersion": 1,
                "usage": {k: usage[k] for k in ("cost", "prompt_tokens", "completion_tokens") if k in usage}}
            metadata.write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
            if provenance["returnedModel"] != config["model"]:
                raise ValueError("Returned model differs from requested Gemini model; raw retained for " + key)
        image = Image.open(io.BytesIO(data)); image.load()
        conformed = conform(image)
        destination.parent.mkdir(parents=True, exist_ok=True)
        conformed.save(destination, format="PNG")
        provenance.update(conformanceVersion=3, outputPath=destination.relative_to(out_root).as_posix(),
                          outputSha256=hashlib.sha256(destination.read_bytes()).hexdigest())
        metadata.write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
        results.append((key, destination.relative_to(out_root).as_posix()))
        print("Generated" if not args.reconform else "Reconformed", key,
              "raw", provenance["rawSha256"], flush=True)
    manifest_path = out_root / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    manifest["assets"].update(dict(results))
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0
