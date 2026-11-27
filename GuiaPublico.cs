using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal sealed class ComandoDoMix
{
    public string Grupo { get; private set; }
    public string Comando { get; private set; }
    public string Descricao { get; private set; }
    public string Exemplo { get; private set; }
    public string DicaAcao { get { return "Copiar " + Exemplo; } }

    internal ComandoDoMix(string grupo, string comando, string descricao, string exemplo = null)
    {
        Grupo = grupo;
        Comando = comando;
        Descricao = descricao;
        Exemplo = exemplo ?? comando;
    }
}

internal static class GuiaComandos
{
#if PERFIL_REMOTO
    private static ComandoDoMix[] Todos { get { return PerfilRemoto.Perfil.Comandos; } }
    internal static readonly string[] Grupos = new[] { "Todas as categorias" }
        .Concat(Todos.Select(c => c.Grupo).Distinct()).ToArray();
#else
    internal static readonly string[] Grupos = { "Todos", "Ajuda", "Fila", "Ranking" };
    private static readonly ComandoDoMix[] Todos = {
        new ComandoDoMix("Ajuda", "/ajuda", "Abre a ajuda do jogo."),
        new ComandoDoMix("Fila", "!ready", "Marca o jogador como pronto."),
        new ComandoDoMix("Ranking", "!rank", "Consulta a classificação do jogador.")
    };
#endif

    internal static IEnumerable<ComandoDoMix> Filtrar(int grupo, string termo)
    {
        string busca = (termo ?? "").Trim();
        return Todos.Where(item => (grupo == 0 || item.Grupo == Grupos[grupo]) &&
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(item.Comando + " " + item.Descricao + " " + item.Grupo,
                busca, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
