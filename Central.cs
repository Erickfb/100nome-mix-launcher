using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal sealed partial class Launcher
{
    private static readonly Uri CanalDiscord = new Uri(PerfilInstancia.CanalDiscord);
    private static readonly CultureInfo Portugues = CultureInfo.GetCultureInfo("pt-BR");
    private readonly CancellationTokenSource fecharCentral = new CancellationTokenSource();
    private DateTime ultimaConsultaNoticias = DateTime.MinValue;
    private EstatisticasMix historico;
    private DateTime horaHistorico;
    private string arteHistorico;

    private void ConfigurarCentral()
    {
        foreach (string pagina in new[] { "Servidor", "Noticias", "Estatisticas", "Comandos" })
        {
            string destino = pagina;
            Controle<RadioButton>("Aba" + destino).Checked += delegate { MostrarPagina(destino); };
        }
        Controle<Image>("ArteNoticia").Source = Imagem(Cenarios.Recurso("de_ancient", recursos));
        Controle<TextBlock>("VersaoNoticia").Text = "MIX 100NOME  ·  VERSÃO " + Atualizacao.Versao;
        Controle<ItemsControl>("MuralMix").ItemsSource = MuralDaComunidade.Noticias;
        var url = Atualizacao.NotasDaVersao;
        if (url != null)
        {
            var notas = Controle<Button>("AbrirNotasVersao");
            notas.ToolTip = url.AbsoluteUri;
            notas.Click += delegate { AbrirNoNavegador(url); };
            notas.Visibility = Visibility.Visible;
        }
        Controle<Button>("AbrirDiscord").Click += delegate { AbrirNoNavegador(CanalDiscord); };
        Controle<Button>("AtualizarNoticias").Click += delegate { CarregarNoticias(true); };
        Controle<Button>("AtualizarHistorico").Click += delegate { Atualizar(); CarregarPatente(); };
        Controle<Button>("AlternarCatalogo").Click += delegate { AlternarCatalogo(); };
        Controle<Button>("CopiarDesempenho").Click += delegate { Copiar("!desempenho", "Comando !desempenho copiado."); };
        Controle<ItemsControl>("ListaSteam").AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
        {
            var botao = e.OriginalSource as Button;
            Uri link = botao == null ? null : botao.Tag as Uri;
            if (link != null) AbrirNoNavegador(link);
        }));
        Janela.SizeChanged += delegate { AjustarLayoutCentral(Janela.ActualWidth); };
        AjustarLayoutCentral(Janela.Width);
    }

    private void AbrirNoNavegador(Uri link)
    {
        if (link == null || !link.IsAbsoluteUri || link.Scheme != Uri.UriSchemeHttps ||
            !String.IsNullOrEmpty(link.UserInfo))
        {
            Informar("Confira o endereço HTTPS configurado para este link.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { Informar("Falha ao abrir o link. Confira o navegador padrão do Windows."); }
        catch (InvalidOperationException) { Informar("O navegador padrão não conseguiu abrir o link."); }
    }

    private void MostrarPagina(string pagina)
    {
        bool servidor = pagina == "Servidor";
        Controle<Grid>("Hero").Visibility = servidor ? Visibility.Visible : Visibility.Collapsed;
        Controle<StackPanel>("ResumoServidor").Visibility = servidor ? Visibility.Visible : Visibility.Collapsed;
        Controle<Grid>("Guia").Visibility = pagina == "Comandos" ? Visibility.Visible : Visibility.Collapsed;
        Controle<Grid>("Noticias").Visibility = pagina == "Noticias" ? Visibility.Visible : Visibility.Collapsed;
        Controle<Grid>("Estatisticas").Visibility = pagina == "Estatisticas" ? Visibility.Visible : Visibility.Collapsed;
        if (pagina == "Noticias") CarregarNoticias(false);
        if (pagina == "Estatisticas") CarregarPatente();
    }

    private void AjustarLayoutCentral(double largura)
    {
        bool estreito = largura < 740;
        var acoes = Controle<StackPanel>("AcoesResumo");
        bool empilhar = largura < 550;
        Grid.SetRow(acoes, empilhar ? 1 : 0);
        acoes.HorizontalAlignment = empilhar ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        acoes.Margin = empilhar ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        Controle<StackPanel>("SubtituloMarca").Visibility = estreito ? Visibility.Collapsed : Visibility.Visible;
        Controle<StackPanel>("TituloMarcaAgrupado").Visibility = largura < 530 ? Visibility.Collapsed : Visibility.Visible;
        var colunas = Controle<Grid>("ColunasNoticias");
        colunas.ColumnDefinitions[1].Width = estreito ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        var steam = Controle<Border>("PainelSteam");
        Grid.SetColumn(steam, estreito ? 0 : 1);
        Grid.SetRow(steam, estreito ? 1 : 0);
        steam.Margin = estreito ? new Thickness(0, 16, 0, 0) : new Thickness(17, 0, 0, 0);
        Controle<System.Windows.Controls.Primitives.UniformGrid>("CartoesEstatisticas").Columns = largura < 660 ? 2 : 4;
        Controle<ItemsControl>("CatalogoPatentes").Tag = largura < 460 ? 1 : largura < 680 ? 2 : 3;
    }

    private async void CarregarNoticias(bool manual)
    {
        var botao = Controle<Button>("AtualizarNoticias");
        if (!botao.IsEnabled || fecharCentral.IsCancellationRequested) return;
        if (!manual && DateTime.UtcNow - ultimaConsultaNoticias < TimeSpan.FromMinutes(5)) return;
        ultimaConsultaNoticias = DateTime.UtcNow;
        botao.IsEnabled = false;
        var lista = Controle<ItemsControl>("ListaSteam");
        string avisoNoticias = "A Steam está indisponível. Tente atualizar mais tarde.";
        EstadoDasNoticias("Consultando a Steam…");
        try
        {
            var boletim = await NoticiasSteam.ConsultarAsync(fecharCentral.Token);
            if (fecharCentral.IsCancellationRequested) return;
            if (boletim.Itens.Length > 0)
            {
                lista.ItemsSource = boletim.Itens.Select(n => new {
                    n.Titulo, n.Link, Data = n.Publicado.ToLocalTime().ToString("d 'de' MMMM 'de' yyyy", Portugues)
                }).ToArray();
                avisoNoticias = boletim.EmCache ? "Exibindo notícias salvas neste computador." : "";
            }
            else if (lista.Items.Count > 0) avisoNoticias = "Sem nova consulta. As notícias abaixo são da última leitura.";
        }
        catch (OperationCanceledException) { if (fecharCentral.IsCancellationRequested) return; }
        catch (Exception) { if (lista.Items.Count > 0) avisoNoticias = "Sem nova consulta. As notícias abaixo são da última leitura."; }
        finally { if (!fecharCentral.IsCancellationRequested) botao.IsEnabled = true; }
        EstadoDasNoticias(avisoNoticias);
    }

    private void EstadoDasNoticias(string valor)
    {
        var estado = Controle<TextBlock>("EstadoNoticias");
        estado.Text = valor;
        estado.Visibility = valor.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MostrarHistorico(EstadoServidor info)
    {
        var leitura = info == null ? null : info.Estatisticas;
        if (leitura != null)
        {
            historico = leitura;
            horaHistorico = DateTime.Now;
        }
        var estado = Controle<TextBlock>("EstadoHistorico");
        string horario = horaHistorico.ToString("HH:mm:ss", Portugues);
        if (leitura != null) estado.Text = "Atualizado às " + horario + ".";
        else
        {
            string motivo = info == null ? "O servidor não respondeu." : "O servidor ainda não disponibilizou o histórico.";
            estado.Text = historico == null ? motivo : "Última leitura às " + horario + ". " + motivo;
        }
        Controle<TextBlock>("HistoricoPartidas").Text = historico == null ? "—" : historico.Partidas.ToString("N0", Portugues);
        Controle<TextBlock>("HistoricoJogadores").Text = historico == null ? "—" : historico.Jogadores.ToString("N0", Portugues);
        Controle<TextBlock>("HistoricoRodadas").Text = historico == null ? "—" : historico.Rodadas.ToString("N0", Portugues);
        Controle<TextBlock>("HistoricoEmpates").Text = historico == null ? "—" : historico.Empates.ToString("N0", Portugues);

        bool partida = historico != null && historico.Partidas > 0 && !string.IsNullOrEmpty(historico.UltimoMapa);
        var titulo = Controle<TextBlock>("MapaUltimaPartida");
        var detalhe = Controle<TextBlock>("DetalheUltimaPartida");
        titulo.Text = partida ? NomeMapa(historico.UltimoMapa) : historico == null ? "Histórico indisponível" :
            historico.Partidas == 0 ? "Ainda não há partidas concluídas" : "Última partida indisponível";
        detalhe.Text = partida ? "Placar final na ordem dos times escalados." :
            historico != null && historico.Partidas == 0 ? "O primeiro mix competitivo concluído aparece aqui." : "";
        detalhe.Visibility = detalhe.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Controle<TextBlock>("PlacarUltimaPartida").Text = partida ? historico.PlacarA + " × " + historico.PlacarB : "";
        var imagem = Controle<Image>("ArteUltimaPartida");
        string recurso = partida ? Cenarios.Recurso(historico.UltimoMapa, recursos) : Cenarios.Padrao;
        imagem.Visibility = recurso == Cenarios.Padrao ? Visibility.Collapsed : Visibility.Visible;
        if (recurso != Cenarios.Padrao && recurso != arteHistorico)
        {
            imagem.Source = Imagem(recurso);
            AutomationProperties.SetName(imagem, "Cenário de " + NomeMapa(historico.UltimoMapa));
            arteHistorico = recurso;
        }
    }
}
