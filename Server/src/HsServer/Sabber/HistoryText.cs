using System.Text;
using HsServer.Engine;
using PegasusGame;

namespace HsServer.Sabber;

/// <summary>Client power history as Power.log-style text (for comparing our output with live logs).</summary>
internal static class HistoryText
{
    /// <param name="withTags">Also list the tags of FULL_ENTITY / SHOW_ENTITY / CHANGE_ENTITY (as Power.log does).</param>
    public static string Format(IEnumerable<PowerHistoryData> history, Func<int, string> name, bool withTags = false)
    {
        var sb = new StringBuilder();
        int depth = 0;
        string Pad() => new string(' ', depth * 4);
        void Tags(IEnumerable<Tag> tags)
        {
            if (withTags)
                foreach (Tag t in tags) sb.AppendLine($"{Pad()}    tag={TagName(t.Name)} value={Value(t.Name, t.Value)}{Note(t.Name)}");
        }
        // tag lists (Power.log never prints them): CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED (Twinspell's copy)...
        void Lists(IEnumerable<TagList> lists, string kind = "taglist")
        {
            if (withTags && lists != null)
                foreach (TagList l in lists) sb.AppendLine($"{Pad()}    {kind}={TagName(l.Name)} values=[{string.Join(",", l.Values)}]{Note(l.Name)}");
        }
        foreach (PowerHistoryData d in history)
        {
            if (d.PowerStart is { } s)
            {
                string flags = (s.TriggerKeyword != 0 ? $" TriggerKeyword={TagName(s.TriggerKeyword)}" : "") +
                               (s.ShowInHistory ? " ShowInHistory" : "") + (s.IsDeferrable ? " IsDeferrable" : "") +
                               (s.IsBatchable ? " IsBatchable" : "") + (s.IsDeferBlocker ? " IsDeferBlocker" : "") +
                               (s.ForceShowBigCard ? " ForceShowBigCard" : "") + (string.IsNullOrEmpty(s.EffectCardId) ? "" : $" EffectCardId={s.EffectCardId}");
                sb.AppendLine($"{Pad()}BLOCK_START BlockType={s.Type} Entity={name(s.Source)} Target={(s.Target == 0 ? "0" : name(s.Target))} EffectIndex={s.EffectIndex}{flags}");
                depth++;
            }
            else if (d.PowerEnd != null) { depth = Math.Max(0, depth - 1); sb.AppendLine($"{Pad()}BLOCK_END"); }
            else if (d.TagChange is { } t) sb.AppendLine($"{Pad()}TAG_CHANGE Entity={name(t.Entity)} tag={TagName(t.Tag)} value={Value(t.Tag, t.Value)}{Note(t.Tag)}");
            else if (d.MetaData is { } m) sb.AppendLine($"{Pad()}META_DATA - Meta={m.Type} Data={m.Data} Info=[{string.Join(",", m.Info.Select(name))}]");
            else if (d.FullEntity is { } f) { sb.AppendLine($"{Pad()}FULL_ENTITY - Creating ID={f.Entity} CardID={f.Name}" + (withTags ? "" : $" ({f.Tags.Count} tags)")); Tags(f.Tags); Lists(f.TagLists); Lists(f.DefTagLists, "deftaglist"); }
            else if (d.ShowEntity is { } sh) { sb.AppendLine($"{Pad()}SHOW_ENTITY - Updating Entity={name(sh.Entity)} CardID={sh.Name}"); Tags(sh.Tags); Lists(sh.TagLists); Lists(sh.DefTagLists, "deftaglist"); }
            else if (d.TagListChange is { } tl) sb.AppendLine($"{Pad()}TAG_LIST_CHANGE Entity={name(tl.Entity)} tag={TagName(tl.Tag)} values=[{string.Join(",", tl.TagList)}]{(tl.ChangeDef ? " def" : "")}{Note(tl.Tag)}");
            else if (d.HideEntity is { } h) sb.AppendLine($"{Pad()}HIDE_ENTITY - Entity={name(h.Entity)} tag=ZONE value={(TAG_ZONE)h.Zone}");
            else if (d.ChangeEntity is { } c) { sb.AppendLine($"{Pad()}CHANGE_ENTITY - Updating Entity={name(c.Entity)} CardID={c.Name}"); Tags(c.Tags); }
            else if (d.SubSpellStart is { } ss)
            {
                sb.AppendLine($"{Pad()}SUB_SPELL_START - SpellPrefabGUID={ss.SpellPrefabGuid} Source={(ss.SourceEntityId == 0 ? "0" : name(ss.SourceEntityId))} Targets=[{string.Join(",", ss.TargetEntityIds.Select(name))}]");
                depth++;
            }
            else if (d.SubSpellEnd != null) { depth = Math.Max(0, depth - 1); sb.AppendLine($"{Pad()}SUB_SPELL_END"); }
            else if (d.CreateGame != null) sb.AppendLine("CREATE_GAME");
            else if (d.ShuffleDeck is { } sd) sb.AppendLine($"{Pad()}SHUFFLE_DECK PlayerID={sd.PlayerId}");
        }
        return sb.ToString();
    }

    private static string TagName(int tag) => Enum.IsDefined(typeof(GAME_TAG), tag) ? ((GAME_TAG)tag).ToString() : tag.ToString();

    // A tag the client doesn't name keeps its number in tag= (the convention: the client doesn't read it); HearthSim's name for it,
    // when known, follows at the line's end (Server/data/hearthsim-tag-names.tsv, `HsServer hsjson`).
    private static string Note(int tag) => Enum.IsDefined(typeof(GAME_TAG), tag) ? "" : Tools.TagNames.Note(tag) is { } n ? $"  # hs:{n}" : "";

    private static string Value(int tag, int value) => (GAME_TAG)tag switch
    {
        GAME_TAG.ZONE => ((TAG_ZONE)value).ToString(),
        GAME_TAG.STEP or GAME_TAG.NEXT_STEP => ((TAG_STEP)value).ToString(),
        GAME_TAG.PLAYSTATE => ((TAG_PLAYSTATE)value).ToString(),
        _ => value.ToString(),
    };
}
