using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

internal enum UsoEntrada { Medir, Jogar }

internal abstract class CanalJogo : IDisposable
{
    internal static bool ErroDeRede(Exception erro)
    {
        return erro is IOException || erro is InvalidDataException || erro is SocketException || erro is WebException ||
            erro is WebSocketException || erro is OperationCanceledException || erro is ObjectDisposedException ||
            erro is CryptographicException || erro is UnauthorizedAccessException || erro is PlatformNotSupportedException ||
            erro is System.Text.DecoderFallbackException;
    }

    internal static async Task<CanalJogo> AbrirAsync(bool udp, UsoEntrada uso, CancellationToken token)
    {
        if (udp) return await CanalUdp.AbrirPilotoAsync(uso, token).ConfigureAwait(false);
        return await CanalWebSocket.AbrirPonteAsync(uso, token).ConfigureAwait(false);
    }

    internal static async Task<CanalJogo> AbrirValidadoAsync(ulong id, bool udp, UsoEntrada uso, CancellationToken token)
    {
        var canal = await AbrirAsync(udp, uso, token).ConfigureAwait(false);
        try
        {
            var estado = await Task.Run(() => canal.Consultar(token), token).ConfigureAwait(false);
            if (id == 0 || estado.SteamId != id) throw new InvalidDataException("Identidade do mix não confirmada.");
            return canal;
        }
        catch { canal.Dispose(); throw; }
    }

    private byte[] ultimaInfo;
    private int indicePorta;
    internal EstadoServidor Consultar(CancellationToken token)
    {
        var estado = ConsultaServidor.Consultar(delegate(byte[] pedido, int tamanho, byte[] resposta)
        {
            token.ThrowIfCancellationRequested();
            int n = Trocar(pedido, tamanho, resposta, token);
            if (n <= 0) throw new IOException("A entrada não respondeu.");
            if (n > 4 && resposta[4] == 0x49)
            {
                ultimaInfo = new byte[n];
                Buffer.BlockCopy(resposta, 0, ultimaInfo, 0, n);
            }
            return n;
        });
        indicePorta = estado.IndicePorta;
        return estado;
    }

    internal byte[] InfoLocal(int porta)
    {
        byte[] info = (byte[])ultimaInfo.Clone();
        // O browser da Steam usa a porta de jogo anunciada no EDF para entrar.
        if (indicePorta >= 0)
        {
            info[indicePorta] = (byte)(porta & 255);
            info[indicePorta + 1] = (byte)(porta >> 8);
        }
        return info;
    }

    protected abstract int Trocar(byte[] pedido, int tamanho, byte[] resposta, CancellationToken token);
    internal abstract Task EnviarAsync(byte[] dados, int tamanho, CancellationToken token);
    internal abstract Task LerAsync(Action<byte[], int> entregar, CancellationToken token);
    public abstract void Dispose();
}
