using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed partial class Launcher
{
    private readonly ImageSource[] artesPatentes = new ImageSource[PatenteDoMix.Catalogo.Length];
    private CancellationTokenSource consultaPatente;
    private ulong? contaDaPatente;

    private ImageSource ArteDaPatente(PatenteDoMix patente)
    {
        if (artesPatentes[patente.Indice] == null)
            artesPatentes[patente.Indice] = Imagem("patente.skillgroup" + patente.IdDaFaixa.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return artesPatentes[patente.Indice];
    }

    private void AlternarCatalogo()
    {
        var lista = Controle<ItemsControl>("CatalogoPatentes");
        bool abrir = lista.Visibility != Visibility.Visible;
        if (abrir && lista.ItemsSource == null)
        {
            lista.ItemsSource = PatenteDoMix.Catalogo.Select(p => new {
                p.Nome, Arte = ArteDaPatente(p), Legenda = p.PontosMinimos + " pts"
            }).ToArray();
        }
        lista.Visibility = abrir ? Visibility.Visible : Visibility.Collapsed;
        Controle<Button>("AlternarCatalogo").Content = abrir ? "Recolher patentes" : "Ver as 18 patentes";
    }

    private void ConferirContaDaPatente()
    {
        if (Controle<RadioButton>("AbaEstatisticas").IsChecked == true && contaDaPatente != CadastroAutomatico.ContaSteamAtiva())
            CarregarPatente();
    }

    private async void CarregarPatente()
    {
        if (fecharCentral.IsCancellationRequested) return;
        ulong conta = CadastroAutomatico.ContaSteamAtiva();
        if (consultaPatente != null)
        {
            if (contaDaPatente == conta) return;
            consultaPatente.Cancel();
        }
        contaDaPatente = conta;
        if (conta == 0)
        {
            EstadoDaPatente("Steam fechada ou desconectada", "As 18 faixas do mix estão disponíveis na coleção abaixo.");
            return;
        }
        var pedido = CancellationTokenSource.CreateLinkedTokenSource(fecharCentral.Token);
        consultaPatente = pedido;
        EstadoDaPatente("Consultando patente…", "");
        try
        {
            var leitura = await PatentePessoal.ConsultarAsync(conta, pedido.Token);
            if (!pedido.IsCancellationRequested && conta == CadastroAutomatico.ContaSteamAtiva()) MostrarPatente(leitura);
        }
        catch (Exception) { if (!pedido.IsCancellationRequested) PatenteIndisponivel(); }
        finally
        {
            if (consultaPatente == pedido) consultaPatente = null;
            pedido.Dispose();
        }
    }

    private void MostrarPatente(PatenteDoMix faixa)
    {
        if (faixa == null) { PatenteIndisponivel(); return; }
        EstadoDaPatente(faixa.Nome, "Consulte seus pontos e o progresso com !rank no jogo.");
        var imagem = Controle<Image>("DistintivoPessoal");
        imagem.Source = ArteDaPatente(faixa);
        AutomationProperties.SetName(imagem, faixa.Nome);
        imagem.Visibility = imagem.Source == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PatenteIndisponivel()
    {
        EstadoDaPatente("Patente indisponível", "Tente consultar o servidor novamente em Atualizar.");
    }

    private void EstadoDaPatente(string titulo, string detalhe)
    {
        var imagem = Controle<Image>("DistintivoPessoal");
        imagem.Source = null;
        imagem.Visibility = Visibility.Collapsed;
        Controle<TextBlock>("TituloPatente").Text = titulo;
        var texto = Controle<TextBlock>("DetalhePatente");
        texto.Text = detalhe;
        texto.Visibility = detalhe.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
