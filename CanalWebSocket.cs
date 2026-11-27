using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

// Uma mensagem WebSocket binária representa um datagrama para o CS2 do mix.
internal sealed class CanalWebSocket : CanalJogo
{
    internal const int TamanhoMaximo = 65507;
    private const string Protocolo = "100nome-cs2-v1";
    private readonly ClientWebSocket socket = new ClientWebSocket();

    internal static async Task<CanalWebSocket> AbrirPonteAsync(UsoEntrada uso, CancellationToken token)
    {
        var canal = new CanalWebSocket();
        try
        {
            var ticket = await Task.Run(() => AutorizacaoRota.ObterTicket("ws", uso, token), token).ConfigureAwait(false);
            canal.socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
            canal.socket.Options.AddSubProtocol(Protocolo);
            canal.socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            var uri = new Uri("wss://" + EntradaMix.Ponte.Dominio + "/v3/ws/" + AutorizacaoRota.Finalidade(uso));
            using (var prazo = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                prazo.CancelAfter(3000);
                await canal.socket.ConnectAsync(uri, prazo.Token).ConfigureAwait(false);
            }
            if (canal.socket.SubProtocol != Protocolo) throw new InvalidDataException("Protocolo da rota incompatível.");
            return canal;
        }
        catch { canal.Dispose(); throw; }
    }

    internal override Task EnviarAsync(byte[] dados, int tamanho, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open) throw new EndOfStreamException();
        return socket.SendAsync(new ArraySegment<byte>(dados, 0, tamanho), WebSocketMessageType.Binary, true, token);
    }

    internal async Task<int> ReceberAsync(byte[] dados, CancellationToken token)
    {
        int usados = 0;
        do
        {
            var parte = await socket.ReceiveAsync(new ArraySegment<byte>(dados, usados, dados.Length - usados), token).ConfigureAwait(false);
            if (parte.MessageType != WebSocketMessageType.Binary) throw new EndOfStreamException();
            usados += parte.Count;
            if (parte.EndOfMessage && usados != 0) return usados;
            if (parte.EndOfMessage || usados == dados.Length) throw new InvalidDataException("Datagrama inválido.");
        }
        while (true);
    }

    internal override async Task LerAsync(Action<byte[], int> entregar, CancellationToken token)
    {
        var dados = new byte[TamanhoMaximo];
        while (true)
        {
            int tamanho = await ReceberAsync(dados, token).ConfigureAwait(false);
            entregar(dados, tamanho);
        }
    }

    protected override int Trocar(byte[] pedido, int tamanho, byte[] resposta, CancellationToken token)
    {
        using (var prazo = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            prazo.CancelAfter(900);
            EnviarAsync(pedido, tamanho, prazo.Token).GetAwaiter().GetResult();
            return ReceberAsync(resposta, prazo.Token).GetAwaiter().GetResult();
        }
    }

    public override void Dispose()
    {
        socket.Abort();
        socket.Dispose();
    }
}
