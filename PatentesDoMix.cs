// O servidor e o launcher leem a mesma faixa a partir dos pontos do histórico.
internal sealed class PatenteDoMix
{
    internal readonly int Indice;
    internal int IdDaFaixa { get { return Indice + 1; } }
    internal readonly int PontosMinimos;
    internal readonly string Nome;
    internal readonly string Apelido;

    private PatenteDoMix(int indice, int pontosMinimos, string nome, string apelido = "")
    {
        Indice = indice;
        PontosMinimos = pontosMinimos;
        Nome = nome;
        Apelido = apelido == "" ? nome : apelido;
    }

    internal static readonly PatenteDoMix[] Catalogo = {
        new PatenteDoMix(0, 0, "Prata I"),
        new PatenteDoMix(1, 15, "Prata II"),
        new PatenteDoMix(2, 30, "Prata III"),
        new PatenteDoMix(3, 45, "Prata IV"),
        new PatenteDoMix(4, 60, "Prata de Elite", "Prata Elite"),
        new PatenteDoMix(5, 75, "Prata de Elite Mestre", "Prata Mestre"),
        new PatenteDoMix(6, 90, "Ouro I"),
        new PatenteDoMix(7, 110, "Ouro II"),
        new PatenteDoMix(8, 130, "Ouro III"),
        new PatenteDoMix(9, 150, "Ouro Mestre"),
        new PatenteDoMix(10, 170, "Guardião Mestre I", "AK I"),
        new PatenteDoMix(11, 195, "Guardião Mestre II", "AK II"),
        new PatenteDoMix(12, 220, "Guardião Mestre de Elite", "AK Cruzada"),
        new PatenteDoMix(13, 245, "Exímio Mestre Guardião", "Xerife"),
        new PatenteDoMix(14, 270, "Águia Lendária", "Águia I"),
        new PatenteDoMix(15, 300, "Águia Lendária Mestra", "Águia II"),
        new PatenteDoMix(16, 330, "Mestre Supremo de Primeira Classe", "Supremo"),
        new PatenteDoMix(17, 360, "A Elite Global", "Global")
    };

    internal static PatenteDoMix PorPontos(int pontos)
    {
        for (int i = Catalogo.Length - 1; i > 0; i--)
        {
            if (pontos >= Catalogo[i].PontosMinimos) return Catalogo[i];
        }
        return Catalogo[0];
    }
}
