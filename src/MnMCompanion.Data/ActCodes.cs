namespace MnMCompanion.Data;

/// <summary>What is known about each ledger act code, saved in the database's <c>act_code</c> table so the meaning travels with the rows.</summary>
public sealed record ActCode(string Act, string Meaning, string Certainty, string Notes);

public static class ActCodes
{
    public const string Confirmed = "confirmed", Inferred = "inferred", Unknown = "unknown";

    /// <summary>
    /// Worked out from the rows themselves (checked on 29,760 rows across 76 files, 2026-10-24) and from the game client's own list of
    /// ledger calls. "confirmed" = the payload says it or the client's list does, "inferred" = fits every sample but nothing states it,
    /// "unknown" = not understood, or never seen in a file.
    /// </summary>
    public static IReadOnlyList<ActCode> All { get; } =
    [
        new("act_01", "Level up", Confirmed, "d21 new level, d22 old level. In a Character file it is the file owner; in a Social file another player (f07, f10 class)."),
        new("act_02", "Death", Confirmed, "Character file, the player's own death only. f02 the cause as 'name_…' (empty in every row seen), f05 the zone, f06 the level; no detail fields. The client's LogDeath writes it; the rows fall on the days characters died."),
        new("act_04", "Quest item hand-in (never written)", Confirmed, "The client's ItemTurnIn. Its logger exists but the hand-in window never calls it, so no ledger file has ever had one. Quest text goes to the journal instead."),
        new("act_05", "Kill (never seen)", Confirmed, "The client's Kill (enemy, zone, level, experience). The string exists; no ledger file has it."),
        new("act_11", "Item dropped", Confirmed, "Character file. d04 item, d05 24-hex item id, d03 'user_confirmed_drop'. f02 is an item reference."),
        new("act_12", "Item picked up", Inferred, "Character file. d04 item, d05 item id."),
        new("act_13", "Item looted", Confirmed, "d02 the mob it came from (a kind of mob, not a corpse), d04 item, d05 item id, d01 quantity (always 1). f02 'ref_N' is an item reference, not a corpse id. Social file: other players' loots with f07 looter, f10 class, f06 level."),
        new("act_14", "Coin purse looted", Confirmed, "d13 mob, d12 coin 'pp,gp,sp,cp' (100 per step), d11 total (counts 10 per step, wrong), d14 'npc_corpse' or 'party_split', d15 the file owner's split. Only exists for corpses that had coin."),
        new("act_15", "Item destroyed", Confirmed, "Character file. d04 item, d03 'user_confirmed_destroy'."),
        new("act_16", "Trade completed (items/coin you gave)", Confirmed, "Character file. partner (an id), itemCount, currency 'pp,gp,sp,cp', summary, items list."),
        new("act_17", "Coins picked up (never seen)", Confirmed, "The client's PickupCoins. The string exists; no ledger file has it."),
        new("act_18", "Coins dropped (never seen)", Confirmed, "The client's DropCoins. The string exists; no ledger file has it."),
        new("act_19", "Coins destroyed (never seen)", Confirmed, "The client's DestroyCoins. The string exists; no ledger file has it."),
        new("act_20", "Trade received", Confirmed, "Character file. Same fields as act_16; summary 'received_from_trade', items list with publicLinkID, name, quantity."),
        new("act_21", "Guild join, leave or disband (never seen)", Unknown, "One of the client's GuildJoin, GuildLeave and GuildDisband; which of the three codes is which is not known. No ledger file has it."),
        new("act_22", "Guild join, leave or disband (never seen)", Unknown, "One of the client's GuildJoin, GuildLeave and GuildDisband; which of the three codes is which is not known. No ledger file has it."),
        new("act_23", "Guild join, leave or disband (never seen)", Unknown, "One of the client's GuildJoin, GuildLeave and GuildDisband; which of the three codes is which is not known. No ledger file has it."),
        new("act_24", "Sold to a vendor", Confirmed, "Character file. d04 item, d01 quantity, d03 coin words for the whole stack ('0 platinum 0 gold 2 silver 71 copper'). The vendor name (d02) is empty in every row."),
        new("act_27", "Harvest", Confirmed, "Character file. d04 item, d01 quantity, d03 'harvest'. f02 is a reference."),
        new("act_28", "Bought from a vendor", Confirmed, "Character file. d04 item, d01 quantity, d03 coin words for the whole stack. Vendor name empty."),
        new("act_31", "Party created", Confirmed, "Social file. d63 'PartyCreate', d64 group reference, d62 member count (after the event, inferred)."),
        new("act_32", "Joined a party", Confirmed, "Social file. d63 'PartyJoin', f07 who joined. Joining someone else's group writes only your own join row."),
        new("act_33", "Party disbanded", Confirmed, "Social file. d63 'PartyDisband'."),
        new("act_34", "Left a party", Confirmed, "Social file. d63 'PartyLeave'."),
        new("act_35", "Party leader changed", Confirmed, "Social file. d63 'PartyLeaderChange'."),
        new("act_36", "Party invite received", Confirmed, "Social file. The client's PartyInviteReceived: f02 'name_…' (Base64 of the inviter's name), f06 their level, f10 their class; no detail fields."),
    ];
}
