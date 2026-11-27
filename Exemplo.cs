using System;
using System.IO;
using System.Text;

internal static class Exemplo
{
    private static void EscreverTexto(BinaryWriter escritor, string valor)
    {
        escritor.Write(Encoding.UTF8.GetBytes(valor));
        escritor.Write((byte)0);
    }

    // Resposta A2S_INFO fictícia, com três pessoas, um bot e uma partida encerrada.
    private static byte[] RespostaDeExemplo()
    {
        using (var memoria = new MemoryStream())
        using (var escritor = new BinaryWriter(memoria))
        {
            escritor.Write(new byte[] { 255, 255, 255, 255, 0x49, 17 });
            EscreverTexto(escritor, "Servidor de exemplo");
            EscreverTexto(escritor, "de_treino");
            EscreverTexto(escritor, "csgo");
            EscreverTexto(escritor, "Counter-Strike 2");
            escritor.Write((ushort)730);
            escritor.Write(new byte[] { 4, 16, 1, (byte)'d', (byte)'l', 0, 1 });
            EscreverTexto(escritor, "1.0");
            escritor.Write((byte)0x20); // Campo opcional de tags do servidor.
            EscreverTexto(escritor, "m100v1-competitive,s100v1-1-10-23-0z,u100v1-de_treino-13-10z");
            return memoria.ToArray();
        }
    }

    private static void Main()
    {
        byte[] amostra = RespostaDeExemplo();
        EstadoServidor estado = ConsultaServidor.Consultar(delegate(byte[] pedido, int tamanho, byte[] resposta)
        {
            Buffer.BlockCopy(amostra, 0, resposta, 0, amostra.Length);
            return amostra.Length;
        });
        var estatisticas = estado.Estatisticas;
        if (estado.Mapa != "de_treino" || estado.Modo != "Competitivo" || estado.Jogadores != 4 ||
            estado.Bots != 1 || estado.Capacidade != 16 || estatisticas == null ||
            estatisticas.PlacarA != 13 || estatisticas.PlacarB != 10)
            throw new InvalidDataException("O leitor do launcher divergiu da amostra.");

        Console.WriteLine("{0} | {1} | {2} pessoas e {3} bot | Placar da amostra: {4} x {5}",
            Cenarios.Nome(estado.Mapa), estado.Modo, estado.Jogadores - estado.Bots,
            estado.Bots, estatisticas.PlacarA, estatisticas.PlacarB);
    }
}
