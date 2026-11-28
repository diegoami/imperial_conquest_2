const saveKey = "ic2.web.proof.italy-500bc";
const land = [
    [44, 80, 98], [52, 94, 113], [183, 169, 114], [110, 125, 81],
    [131, 126, 113], [208, 189, 137], [96, 141, 139], [96, 141, 139],
    [96, 141, 139], [96, 141, 139], [96, 141, 139], [96, 141, 139]
];

window.ic2 = {
    hasSave: () => localStorage.getItem(saveKey) !== null,
    readSave: () => localStorage.getItem(saveKey),
    writeSave: save => localStorage.setItem(saveKey, save),
    pick(canvas, x, y) {
        const box = canvas.getBoundingClientRect();
        return [
            Math.max(0, Math.min(canvas.width - 1, Math.floor((x - box.left) * canvas.width / box.width))),
            Math.max(0, Math.min(canvas.height - 1, Math.floor((y - box.top) * canvas.height / box.height)))
        ];
    },
    draw(canvas, terrain, cities, armies, x, y) {
        const context = canvas.getContext("2d");
        const image = context.createImageData(canvas.width, canvas.height);
        for (let i = 0; i < terrain.length; i++) {
            const [red, green, blue] = land[terrain[i]] ?? [0, 0, 0];
            image.data.set([red, green, blue, 255], 4 * i);
        }
        context.putImageData(image, 0, 0);
        for (const city of cities) {
            context.fillStyle = city.human ? "#fff3bf" : "#28251e";
            context.fillRect(city.x - 1, city.y - 1, 2, 2);
        }
        for (const army of armies) {
            context.fillStyle = army.human ? "#f9e28a" : "#a6372b";
            context.fillRect(army.x - 1, army.y - 1, 3, 3);
        }
        context.strokeStyle = "#fff";
        context.strokeRect(x - 2, y - 2, 5, 5);
    }
};
