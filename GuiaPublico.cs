using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal sealed class ComandoDoMix
{
    public string Grupo { get; private set; }
    public string Comando { get; private set; }
    public string Descricao { get; private set; }
    public string Exemplo { get { return Comando; } }
    public string DicaAcao { get { return "Copiar " + Comando; } }

    internal ComandoDoMix(string grupo, string comando, string descricao)
    {
        Grupo = grupo;
        Comando = comando;
        Descricao = descricao;
    }
}

internal static class GuiaComandos
{
    internal static readonly string[] Grupos = { "Todos", "Ajuda", "Fila", "Ranking" };
    private static readonly ComandoDoMix[] Todos = {
        new ComandoDoMix("Ajuda", "/ajuda", "Abre a ajuda do jogo."),
        new ComandoDoMix("Fila", "!ready", "Marca o jogador como pronto."),
        new ComandoDoMix("Ranking", "!rank", "Consulta a classificação do jogador.")
    };

    internal static IEnumerable<ComandoDoMix> Filtrar(int grupo, string termo)
    {
        string busca = (termo ?? "").Trim();
        return Todos.Where(item => (grupo == 0 || item.Grupo == Grupos[grupo]) &&
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(item.Comando + " " + item.Descricao + " " + item.Grupo,
                busca, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
