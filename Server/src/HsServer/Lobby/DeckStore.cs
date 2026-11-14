using PegasusShared;
using PegasusUtil;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// The account's decks (headers + card lists), owned by the server so the collection deck builder works:
/// CreateDeck 209 → DeckCreated 217, DeckSetData 222 → DBAction 216, RenameDeck 211 → DeckRenamed 219,
/// DeleteDeck 210 → DeckDeleted 218, GetDeckContents 214 → GetDeckContentsResponse 215.
/// Seeded from the recorded SecondaryClientState (714), persisted to a state file after every change, and written
/// back into the 714 the client receives at login.
/// (Protocol from the client: Network/ConnectAPI/CollectionManager — no live recording of deck edits exists.)
/// </summary>
internal sealed class DeckStore
{
    private readonly object gate = new();
    private readonly List<DeckInfo> decks = new();
    private readonly Dictionary<long, DeckContents> contents = new();
    private readonly string statePath;

    public DeckStore(string statePath) => this.statePath = statePath;

    public int Count { get { lock (gate) return decks.Count; } }

    /// <summary>Load the persisted state; false when there is none yet.</summary>
    public bool Load()
    {
        if (statePath == null || !File.Exists(statePath)) return false;
        var state = PegasusPackets.Deserialize<SecondaryClientState>(File.ReadAllBytes(statePath));
        lock (gate) Replace(state.Decks, state.DeckContents);
        return true;
    }

    public void Seed(SecondaryClientState recorded)
    {
        lock (gate)
        {
            Replace(recorded.Decks, recorded.DeckContents);
            Save();
        }
    }

    /// <summary>Write the current decks into a SecondaryClientState about to be sent to the client.</summary>
    public void ApplyTo(SecondaryClientState state)
    {
        lock (gate)
        {
            state.Decks = decks.Select(Copy).ToList();
            state.DeckContents = contents.Values.Select(Copy).ToList();
            // Contents are always sent in full, so no deck relies on the client's offline cache.
            state.ValidCachedDeckIds = new List<long>();
        }
    }

    /// <summary>Every deck header (copies).</summary>
    public IReadOnlyList<DeckInfo> All() { lock (gate) return decks.Select(Copy).ToList(); }

    /// <summary>DeckSetTemplateSource (332): the deck was built from a premade template (DeckInfo has no template id: its
    /// source type is what the client sees).</summary>
    public bool SetTemplateSource(long deckId)
    {
        lock (gate)
        {
            DeckInfo deck = decks.FirstOrDefault(d => d.Id == deckId);
            if (deck == null) return false;
            deck.SourceType = DeckSourceType.DECK_SOURCE_TYPE_TEMPLATE;
            Save();
            return true;
        }
    }

    public DeckInfo Get(long id)
    {
        lock (gate) return decks.FirstOrDefault(d => d.Id == id) is { } d ? Copy(d) : null;
    }

    /// <summary>Card dbf ids of a deck, one entry per copy.</summary>
    public IReadOnlyList<int> CardIds(long id)
    {
        lock (gate)
            return contents.TryGetValue(id, out var c)
                ? c.Cards.SelectMany(card => Enumerable.Repeat(card.Def.Asset, card.Qty)).ToList()
                : Array.Empty<int>();
    }

    /// <summary>A deck's sideboards: (owner card dbf, sideboard card dbf), one entry per copy, in the deck builder's order
    /// (E.T.C.'s band, Zilliax's modules, Beatrix's minion, King of the Underbelly's Beasts).</summary>
    public IReadOnlyList<(int Owner, int Card)> Sideboard(long id)
    {
        lock (gate)
            return contents.TryGetValue(id, out var c)
                ? c.SideboardCards.Where(s => s.Def != null)
                    .SelectMany(s => Enumerable.Repeat((s.LinkedCardDbId, s.Def.Asset), s.HasQty ? s.Qty : 1)).ToList()
                : Array.Empty<(int, int)>();
    }

    /// <summary>A deck's stored sideboard entries (copies).</summary>
    public List<SideBoardCardData> SideboardCards(long id)
    {
        lock (gate)
            return contents.TryGetValue(id, out var c) ? c.SideboardCards.Select(Copy).ToList() : new List<SideBoardCardData>();
    }

    public DeckCreated Create(CreateDeck request)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        DeckInfo info;
        lock (gate)
        {
            long id = Math.Max(decks.Where(d => d.DeckType == DeckType.NORMAL_DECK).Select(d => d.Id).DefaultIfEmpty(0).Max(), 1_000_000) + 1;
            info = new DeckInfo
            {
                Id = id, Name = request.Name ?? "", Hero = request.Hero, DeckType = request.DeckType, Validity = 0,
                HeroOverride = request.HasHeroOverridden && request.HeroOverridden,
                LastModified = now, CreateDate = now, SeasonId = 0,
                SortOrder = request.SortOrder,
                SourceType = request.HasSourceType ? request.SourceType : DeckSourceType.DECK_SOURCE_TYPE_NORMAL,
                FormatType = request.HasFormatType ? request.FormatType : FormatType.FT_STANDARD,
                RandomHeroUseFavorite = true, RandomCoinUseFavorite = true, RandomPetUseFavorite = true,
            };
            if (request.HasCardBack) info.CardBack = request.CardBack;
            if (request.HasCosmeticCoin) info.CosmeticCoin = request.CosmeticCoin;
            if (request.HasBrawlLibraryItemId) info.BrawlLibraryItemId = request.BrawlLibraryItemId;
            if (request.HasPastedDeckHash) info.PastedDeckHash = request.PastedDeckHash;
            decks.Add(info);
            contents[id] = new DeckContents { DeckId = id, Success = true, Cards = new List<DeckCardData>(), SideboardCards = new List<SideBoardCardData>() };
            Save();
        }
        var created = new DeckCreated { Info = Copy(info) };
        if (request.HasRequestId) created.RequestId = request.RequestId;
        return created;
    }

    public DBAction SetData(DeckSetData request)
    {
        lock (gate)
        {
            DeckInfo info = decks.FirstOrDefault(d => d.Id == request.Deck);
            if (info == null) return DbAction(DatabaseAction.DB_A_SET_DECK, DatabaseResult.DB_E_UNKNOWN, request.Deck);

            // The client sends only the cards that changed (CollectionDeck.GenerateContentChanges): each changed card with its
            // new count per premium, 0 = removed. Unchanged cards are not listed, so the change is applied to the stored deck.
            contents.TryGetValue(info.Id, out DeckContents old);
            var counts = new Dictionary<(int Asset, int Premium), int>();
            foreach (DeckCardData card in old?.Cards ?? new List<DeckCardData>())
                counts[(card.Def.Asset, card.Def.Premium)] = card.Qty;
            foreach (DeckCardData card in request.Cards)
                counts[(card.Def.Asset, card.Def.Premium)] = card.Qty;
            var c = new DeckContents { DeckId = info.Id, Success = true, Cards = new List<DeckCardData>(), SideboardCards = new List<SideBoardCardData>() };
            foreach (var ((asset, premium), qty) in counts.Where(kv => kv.Value > 0))
                c.Cards.Add(new DeckCardData { Def = new CardDef { Asset = asset, Premium = premium }, Qty = qty });
            // Sideboards: the same rule (CollectionDeck.GenerateSideboardContentChanges): each changed sideboard card with its
            // new count per premium (0 = removed), keyed by its owner card (LinkedCardDbId); a new sideboard lists all its
            // cards, every premium included; an owner taken out of the deck sends its cards at 0. Unchanged cards aren't
            // listed, so the change is applied to the stored ones, which keep their order (new cards at the end).
            c.SideboardCards.AddRange(MergeSideboards(old?.SideboardCards, request.SideboardCards));
            contents[info.Id] = c;

            if (request.HasHero) info.Hero = request.Hero;
            if (request.HasHeroOverridden) info.HeroOverride = request.HeroOverridden;
            if (request.UiHeroOverride != null) { info.UiHeroOverride = request.UiHeroOverride.Asset; info.UiHeroOverridePremium = request.UiHeroOverride.Premium; }
            if (request.HasCardBack) info.CardBack = request.CardBack;
            else if (request.HasRemovingCardBack && request.RemovingCardBack) info.CardBack = 0;
            if (request.HasCosmeticCoin) info.CosmeticCoin = request.CosmeticCoin;
            else if (request.HasRemovingCosmeticCoin && request.RemovingCosmeticCoin) info.CosmeticCoin = 0;
            if (request.HasPet) info.Pet = request.Pet;
            else if (request.HasRemovingPet && request.RemovingPet) info.Pet = 0;
            if (request.HasPetVariant) info.PetVariant = request.PetVariant;
            else if (request.HasRemovingPetVariant && request.RemovingPetVariant) info.PetVariant = 0;
            if (request.HasRandomHeroUseFavorite) info.RandomHeroUseFavorite = request.RandomHeroUseFavorite;
            if (request.HasRandomCoinUseFavorite) info.RandomCoinUseFavorite = request.RandomCoinUseFavorite;
            if (request.HasRandomPetUseFavorite) info.RandomPetUseFavorite = request.RandomPetUseFavorite;
            if (request.HasRune1) info.Rune1 = request.Rune1;
            if (request.HasRune2) info.Rune2 = request.Rune2;
            if (request.HasRune3) info.Rune3 = request.Rune3;
            if (request.HasFormatType) info.FormatType = request.FormatType;
            if (request.HasSortOrder) info.SortOrder = request.SortOrder;
            if (request.HasPastedDeckHash) info.PastedDeckHash = request.PastedDeckHash;
            info.LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save();
            return DbAction(DatabaseAction.DB_A_SET_DECK, DatabaseResult.DB_E_SUCCESS, info.Id);
        }
    }

    public DeckRenamed Rename(RenameDeck request)
    {
        lock (gate)
        {
            DeckInfo info = decks.FirstOrDefault(d => d.Id == request.Deck);
            if (info != null)
            {
                info.Name = request.Name ?? "";
                info.LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Save();
            }
        }
        return new DeckRenamed { Deck = request.Deck, Name = request.Name ?? "", IsNameCensored = false };
    }

    public DeckDeleted Delete(DeleteDeck request)
    {
        lock (gate)
        {
            if (decks.RemoveAll(d => d.Id == request.Deck) > 0 | contents.Remove(request.Deck)) Save();
        }
        return new DeckDeleted { Deck = request.Deck };
    }

    public GetDeckContentsResponse GetContents(GetDeckContents request)
    {
        var response = new GetDeckContentsResponse { Decks = new List<DeckContents>() };
        lock (gate)
            foreach (long id in request.DeckId)
                response.Decks.Add(contents.TryGetValue(id, out var c)
                    ? Copy(c)
                    : new DeckContents { DeckId = id, Success = false, Cards = new List<DeckCardData>(), SideboardCards = new List<SideBoardCardData>() });
        return response;
    }

    /// <summary>Stored sideboard entries with the client's changes applied (see SetData); entries at 0 are gone.</summary>
    internal static List<SideBoardCardData> MergeSideboards(IEnumerable<SideBoardCardData> stored, IEnumerable<SideBoardCardData> changes)
    {
        var merged = (stored ?? Enumerable.Empty<SideBoardCardData>()).Where(s => s.Def != null).Select(Copy).ToList();
        foreach (SideBoardCardData change in changes ?? Enumerable.Empty<SideBoardCardData>())
        {
            if (change.Def == null) continue;
            int qty = change.HasQty ? change.Qty : 1;
            int at = merged.FindIndex(s => s.LinkedCardDbId == change.LinkedCardDbId && s.Def.Asset == change.Def.Asset && s.Def.Premium == change.Def.Premium);
            if (qty <= 0)
            {
                if (at >= 0) merged.RemoveAt(at);
            }
            else if (at >= 0) merged[at].Qty = qty;
            else
            {
                SideBoardCardData added = Copy(change);
                added.Qty = qty;
                merged.Add(added);
            }
        }
        return merged;
    }

    private void Replace(IEnumerable<DeckInfo> infos, IEnumerable<DeckContents> deckContents)
    {
        decks.Clear();
        contents.Clear();
        decks.AddRange(infos.Select(Copy));
        foreach (DeckContents c in deckContents) contents[c.DeckId] = Copy(c);
    }

    private void Save()
    {
        if (statePath == null) return;
        var state = new SecondaryClientState { Decks = decks.Select(Copy).ToList(), DeckContents = contents.Values.Select(Copy).ToList() };
        StateFiles.WriteAtomic(statePath, PegasusPackets.Serialize(state));
    }

    private static DBAction DbAction(DatabaseAction action, DatabaseResult result, long deck) =>
        new() { Action = action, Result = result, MetaData = deck };

    // Round-trip copies: keep each message's own field presence without sharing instances between threads.
    private static T Copy<T>(T message) where T : class, IProtoBuf, new() =>
        PegasusPackets.Deserialize<T>(PegasusPackets.Serialize(message));
}
