using System;
using System.Buffers.Binary;
using System.IO;

namespace IC2.Data;

/// <summary>The 4-byte pending diplomatic-offer block in known SAV files: the nation currently
/// proposing a trade or alliance to the nation whose turn is starting, and the relation state being
/// proposed. Set by <c>FUN_00452034</c> at the start of every human seat's turn and shown (not
/// logged) by <c>TPremierForm_StartTurn</c> as e.g. "Greece wants to trade with Rome." Confirmed on
/// two independent nations across five saves. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/pending-offer-block-army-split-and-naupactus.md.
/// <para>
/// That report's prose calls the offset <c>fileLength − 22</c>, but its own worked reproduction
/// (<c>d[-22:-18].hex()</c>, commented as printing <c>0b000100</c> for
/// <c>1_rome_270_winter_11.sav</c>) does not: running that exact slice against the current
/// <c>1_rome_270_winter_11.sav</c> yields <c>00010000</c>, not <c>0b000100</c>. The documented pattern
/// (<c>FF FF 01 00</c> / <c>07 00 01 00</c> / <c>0B 00 01 00</c>) is found one byte earlier, at
/// <c>fileLength − 23</c>, on every one of the five saves the report names
/// (<c>1_rome_270_winter_7.sav</c>, <c>_7_b</c>, <c>_9</c>, <c>_9_b</c>, <c>_11</c>) — and that
/// location also does not overlap <see cref="SaveTurnState"/>'s independently-confirmed
/// <c>currentNation</c> word at trailer offset +36 (<c>fileLength − 19</c>), which <c>fileLength −
/// 22</c> would clip into by one byte. This class uses the offset the bytes and the report's own
/// examples agree on (<c>fileLength − 23</c>), not the prose number; flagged for the research repo.
/// </para></summary>
public sealed class SavePendingOffer
{
    /// <summary>Value of <see cref="ProposingNationIndex"/> when no offer is pending.</summary>
    public const ushort NoOfferSentinel = 0xFFFF;

    private SavePendingOffer(ushort proposingNationIndex, ushort proposedRelationState)
    {
        ProposingNationIndex = proposingNationIndex;
        ProposedRelationState = proposedRelationState;
    }

    /// <summary>The proposing nation's code, or <see cref="NoOfferSentinel"/> (0xFFFF) when no offer
    /// is currently pending.</summary>
    public ushort ProposingNationIndex { get; }

    /// <summary>The relation state being proposed, reusing the 16×16 relation-matrix encoding: 1 =
    /// trade, 2 = alliance. Meaningless when <see cref="HasOffer"/> is false.</summary>
    public ushort ProposedRelationState { get; }

    /// <summary>True when a trade or alliance offer is currently pending.</summary>
    public bool HasOffer => ProposingNationIndex != NoOfferSentinel;

    public static SavePendingOffer Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
            throw new DatDataNotPresentException(
                "The DAT has no pending-offer block. It is turn-in-progress state written by " +
                "FUN_00452034 at a human seat's turn start, not world data present before a New " +
                "Game has ever run. See " +
                "https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/pending-offer-block-army-split-and-naupactus.md.");
        if (data.Length < 23)
            throw new InvalidDataException("Save ends before the pending-offer block.");

        // fileLength - 23, not the report's stated "fileLength - 22" — see this class's remarks.
        var offset = data.Length - 23;
        var proposingNationIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        var proposedRelationState = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2, 2));
        return new SavePendingOffer(proposingNationIndex, proposedRelationState);
    }
}
