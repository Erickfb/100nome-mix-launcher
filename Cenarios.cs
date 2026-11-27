using System;
using System.Globalization;
using System.Linq;

internal sealed class ArenaDoMix
{
    internal readonly string Apelido;
    internal readonly string Nome;
    internal readonly string Mapa;
    internal readonly string Oficina;
    internal readonly string MapaBase;

    private ArenaDoMix(string apelido, string nome, string mapa, string oficina, string mapaBase = "")
    {
        Apelido = apelido;
        Nome = nome;
        Mapa = mapa;
        Oficina = oficina;
        MapaBase = mapaBase;
    }

    internal static readonly ArenaDoMix[] Catalogo = {
        new ArenaDoMix("redline", "Redline", "aim_redline", "3162558887"),
        new ArenaDoMix("aim", "AIM Map", "aim_map", "3084291314"),
        new ArenaDoMix("mirage", "Mirage", "de_mirage_warmup", "3301403898", "de_mirage"),
        new ArenaDoMix("inferno", "Inferno", "de_inferno_1vs1", "3303279898", "de_inferno"),
        new ArenaDoMix("dust2", "Dust II", "de_dust2_warmup_remake", "3305011582", "de_dust2"),
        new ArenaDoMix("nuke", "Nuke", "de_nuke_warmup_remake", "3305013005", "de_nuke"),
        new ArenaDoMix("overpass", "Overpass", "de_overpass_warmup_remake", "3305788621", "de_overpass"),
        new ArenaDoMix("train", "Train", "de_train_1vs1", "3310088370", "de_train"),
        new ArenaDoMix("ancient", "Ancient", "de_ancient_warmup_remake", "3312204744", "de_ancient"),
        new ArenaDoMix("cache", "Cache", "de_cache_1vs1", "3326914366", "de_cache"),
        new ArenaDoMix("vertigo", "Vertigo", "de_vertigo_warmup", "3362473730", "de_vertigo")
    };

#if NET10_0_OR_GREATER
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
#endif
    internal static ArenaDoMix Reconhecer(string mapa)
    {
        if (String.IsNullOrWhiteSpace(mapa)) return null;
        string[] partes = mapa.Trim().Replace('\\', '/').ToLowerInvariant().Split('/');
        string id = Cenarios.Identificador(partes[partes.Length - 1]);
        int oficina = Array.IndexOf(partes, "workshop");
        if (oficina < 0)
            return Catalogo.FirstOrDefault(a => a.Mapa == id);
        if (oficina + 1 >= partes.Length) return null;
        string numero = partes[oficina + 1];
        return Catalogo.FirstOrDefault(a => a.Oficina == numero &&
            (oficina + 2 == partes.Length || a.Mapa == id));
    }
}

internal static class Cenarios
{
    internal const string Padrao = "mapa";

    internal static string Recurso(string mapa, string[] recursos)
    {
        var arena = ArenaDoMix.Reconhecer(mapa);
        string id = arena != null && arena.MapaBase.Length > 0 ? arena.MapaBase : Identificador(mapa);
        string recurso = "mapa." + id;
        return id.Length > 0 && Array.IndexOf(recursos, recurso) >= 0 ? recurso : Padrao;
    }

    internal static string Nome(string mapa)
    {
        var arena = ArenaDoMix.Reconhecer(mapa);
        if (arena != null) return "Arena " + arena.Nome;
        string id = Identificador(mapa);
        switch (id)
        {
            case "": return "—";
            case "de_ancient_night": return "Ancient (Noite)";
            case "de_dust2": return "Dust II";
            case "de_cbble": return "Cobblestone";
            case "de_eldorado": return "El Dorado";
            case "ar_shoots": return "Shoots (Dia)";
            case "ar_shoots_night": return "Shoots (Noite)";
            case "rush_001": return "Complexo";
        }
        if (id.StartsWith("de_", StringComparison.Ordinal) ||
            id.StartsWith("cs_", StringComparison.Ordinal) ||
            id.StartsWith("ar_", StringComparison.Ordinal))
            id = id.Substring(3);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
    }

    internal static string Identificador(string mapa)
    {
        if (String.IsNullOrWhiteSpace(mapa)) return "";
        string id = mapa.Trim().Replace('\\', '/');
        id = id.Substring(id.LastIndexOf('/') + 1).ToLowerInvariant();
        foreach (string extensao in new[] { ".vpk", ".vmap", ".vmap_c", ".bsp" })
            if (id.EndsWith(extensao, StringComparison.Ordinal))
                return id.Substring(0, id.Length - extensao.Length);
        return id;
    }
}
