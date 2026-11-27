using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

internal sealed partial class Launcher
{
    private readonly DispatcherTimer atualizacao = new DispatcherTimer();
    private readonly DispatcherTimer aviso = new DispatcherTimer();
    private EntradaMix entradaSelecionada;
    private bool escolhaManual;
    private ComparacaoEntradas ultimasMedicoes;
    private bool conectando;
    private string legendaEntrada = "Escolha uma entrada na aba Servidor.";
    private SondagemEntradas sondagem;
    private SondagemEntradas medicaoParaConectar;
    private PonteJogo ponte;
    private readonly string[] recursos = typeof(Launcher).Assembly.GetManifestResourceNames();
    private readonly CancellationTokenSource fecharAtualizador = new CancellationTokenSource();
    private string imagemAtual = Cenarios.Padrao;
    private int grupo;
    internal Window Janela { get; private set; }

    private void MontarJanela()
    {
        using (Stream xaml = typeof(Launcher).Assembly.GetManifestResourceStream("interface"))
            Janela = (Window)XamlReader.Load(xaml);

        Janela.Width = Math.Min(Janela.Width, SystemParameters.WorkArea.Width);
        Janela.Height = Math.Min(Janela.Height, SystemParameters.WorkArea.Height);
        Janela.Icon = Imagem("icone");
        Controle<Image>("Marca").Source = Imagem("marca");
        Controle<Image>("Cenario").Source = Imagem(imagemAtual);
        Controle<TextBlock>("EstadoVersao").Text = "Versão " + Atualizacao.Versao;
        Controle<Grid>("BarraTitulo").MouseLeftButtonDown += ArrastarJanela;
        Controle<Button>("Minimizar").Click += delegate { Janela.WindowState = WindowState.Minimized; };
        Controle<Button>("Fechar").Click += delegate { Janela.Close(); };
    }

    internal Launcher()
    {
        MontarJanela();
        Controle<Button>("Entrar").Click += delegate { Entrar(); };
        Controle<Button>("VincularEntrada").Click += delegate { VincularEntrada(); };
        Controle<Button>("Copiar").Click += delegate { CopiarEndereco(); };
        foreach (var entrada in EntradaMix.Todas)
        {
            var opcao = Controle<RadioButton>("OpcaoEntrada" + entrada.Numero);
            opcao.Checked += delegate { if (entradaSelecionada != entrada) SelecionarEntrada(entrada); };
            opcao.Click += delegate { if (!escolhaManual) SelecionarEntrada(entrada); };
        }
        Controle<Button>("UsarMenorPing").Click += delegate
        {
            escolhaManual = false;
            aviso.Stop();
            AtualizarEscolha();
        };
        Controle<Button>("Atualizar").Click += delegate { Atualizar(); };
        AtualizarEscolha();
        ConfigurarGuia();
        ConfigurarCentral();

        atualizacao.Interval = TimeSpan.FromSeconds(1);
        atualizacao.Tick += delegate { Atualizar(); ConferirContaDaPatente(); };
        Janela.Activated += delegate { ConferirContaDaPatente(); AjustarMedicaoAoFoco(); };
        Janela.Deactivated += delegate { PausarMedicao(); };
        Janela.StateChanged += delegate { AjustarMedicaoAoFoco(); };
        aviso.Interval = TimeSpan.FromSeconds(6);
        aviso.Tick += delegate { aviso.Stop(); Controle<TextBlock>("Mensagem").Text = legendaEntrada; };
        Janela.Loaded += delegate
        {
            Atualizacao.SinalizarInterfacePronta();
            AjustarMedicaoAoFoco();
            VerificarVersao();
        };
        Janela.Closing += delegate
        {
            fecharAtualizador.Cancel();
            fecharCentral.Cancel();
            PausarMedicao();
            if (medicaoParaConectar != null)
            {
                medicaoParaConectar.Dispose();
                medicaoParaConectar = null;
            }
            if (Application.Current != null && ponte != null && !ponte.Termino.IsCompleted)
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        };
        Janela.Closed += async delegate
        {
            aviso.Stop();
            if (Application.Current != null && Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
            {
                if (ponte != null) await ponte.Termino;
                Application.Current.Shutdown();
            }
        };
    }

    private void ConfigurarGuia()
    {
        Controle<TextBox>("Busca").TextChanged += delegate { FiltrarComandos(); };
        var filtros = Controle<WrapPanel>("Filtros");
        for (int i = 0; i < GuiaComandos.Grupos.Length; i++)
        {
            var escolha = new RadioButton {
                Content = i == 0 ? "Todos" : GuiaComandos.Grupos[i], GroupName = "Categoria", Tag = i,
                Style = (Style)Janela.FindResource("aba"), FontSize = 12,
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 3, 2)
            };
            escolha.Checked += delegate(object sender, RoutedEventArgs e) {
                grupo = (int)((RadioButton)sender).Tag;
                FiltrarComandos();
            };
            filtros.Children.Add(escolha);
            if (i == 0) escolha.IsChecked = true;
        }
        Controle<ItemsControl>("ListaComandos").AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
        {
            var botao = e.OriginalSource as Button;
            if (botao == null || !(botao.Tag is string)) return;
            Copiar((string)botao.Tag, "Comando copiado.");
        }));
        Janela.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Controle<RadioButton>("AbaComandos").IsChecked = true;
                Controle<TextBox>("Busca").Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Controle<RadioButton>("AbaServidor").IsChecked != true)
            {
                Controle<RadioButton>("AbaServidor").IsChecked = true;
                Controle<RadioButton>("AbaServidor").Focus();
                e.Handled = true;
            }
        };
    }

    private void FiltrarComandos()
    {
        string termo = Controle<TextBox>("Busca").Text;
        var itens = GuiaComandos.Filtrar(grupo, termo).ToArray();
        Controle<ItemsControl>("ListaComandos").ItemsSource = itens;
        Controle<TextBlock>("DicaBusca").Visibility = termo.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Controle<TextBlock>("SemResultados").Visibility = itens.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Controle<ScrollViewer>("RolagemGuia").ScrollToTop();
    }

    private async void VerificarVersao()
    {
        var estado = Controle<TextBlock>("EstadoVersao");
        if (Atualizacao.GerenciadaPelaStore)
        {
            estado.ToolTip = "A Microsoft Store atualiza esta instalação do launcher.";
            return;
        }
        if (!Atualizacao.TemCanal)
        {
            estado.ToolTip = "Esta compilação usa atualização manual.";
            return;
        }
        string versaoInstalada = estado.Text;
        estado.ToolTip = "Verificando atualizações do launcher…";
        try
        {
            bool disponivel = await Atualizacao.VerificarAsync(fecharAtualizador.Token);
            if (fecharAtualizador.IsCancellationRequested) return;
            if (disponivel)
            {
                estado.Text = "Aplicando atualização…";
                if (Atualizacao.AtivarPendente())
                {
                    Janela.Close();
                    return;
                }
                estado.Text = versaoInstalada;
                estado.ToolTip = "Atualização disponível. Abra o launcher novamente para tentar aplicá-la.";
            }
            else estado.ToolTip = "Verificação de versão concluída.";
        }
        catch (OperationCanceledException) { if (!fecharAtualizador.IsCancellationRequested) estado.ToolTip = "A consulta de versão expirou."; }
        catch (Exception) { estado.Text = versaoInstalada; estado.ToolTip = "Atualização indisponível. A próxima abertura tentará novamente."; }
    }

    private T Controle<T>(string nome) where T : FrameworkElement
    {
        return (T)Janela.FindName(nome);
    }

    private static BitmapSource Imagem(string nome)
    {
        using (Stream arquivo = typeof(Launcher).Assembly.GetManifestResourceStream(nome))
        {
            if (arquivo == null) return null;
            var imagem = BitmapFrame.Create(arquivo, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            imagem.Freeze();
            return imagem;
        }
    }

    private void ArrastarJanela(object sender, MouseButtonEventArgs e)
    {
        // Os próprios botões da barra tratam o clique sem iniciar o arraste.
        if (e.LeftButton == MouseButtonState.Pressed)
            Janela.DragMove();
    }

    private void AjustarMedicaoAoFoco()
    {
        if (!Janela.IsLoaded || !Janela.IsActive || Janela.WindowState == WindowState.Minimized || fecharCentral.IsCancellationRequested)
        {
            PausarMedicao();
            return;
        }
        if (sondagem != null) return;
        sondagem = medicaoParaConectar ?? new SondagemEntradas(rotas => { if (sondagem != null) MostrarEstado(rotas); });
        MostrarEstado(sondagem.Atual);
        atualizacao.Start();
        Atualizar();
    }

    private void PausarMedicao()
    {
        atualizacao.Stop();
        if (sondagem != null && sondagem != medicaoParaConectar) sondagem.Dispose();
        sondagem = null;
    }

    private void Atualizar()
    {
        if (sondagem != null) sondagem.Medir();
    }

    private async Task<ComparacaoEntradas> VerificarParaConectar(SondagemEntradas medicao, MedicaoEntrada escolha)
    {
        // Um clique em Entrar continua válido se a Steam ou outra janela tomar o foco.
        medicaoParaConectar = medicao;
        try
        {
            medicao.Medir();
            return await medicao.AguardarEscolhaAsync(escolha);
        }
        finally
        {
            if (medicaoParaConectar == medicao)
            {
                medicaoParaConectar = null;
                if (medicao != sondagem) medicao.Dispose();
            }
        }
    }

    private void SelecionarEntrada(EntradaMix entrada)
    {
        escolhaManual = true;
        entradaSelecionada = entrada;
        aviso.Stop();
        AtualizarEscolha();
    }

    private void AtualizarEscolha()
    {
        if (!escolhaManual && !conectando)
        {
            var melhor = ultimasMedicoes == null ? null : ultimasMedicoes.MelhorConfirmada(entradaSelecionada);
            entradaSelecionada = melhor == null ? null : melhor.Entrada;
        }
        foreach (var entrada in EntradaMix.Todas)
        {
            var opcao = Controle<RadioButton>("OpcaoEntrada" + entrada.Numero);
            opcao.IsEnabled = !conectando && ultimasMedicoes != null && ultimasMedicoes.MedicaoDe(entrada).PodeConectar;
            opcao.IsChecked = entrada == entradaSelecionada;
        }
        var selecionada = entradaSelecionada == null || ultimasMedicoes == null ? null : ultimasMedicoes.MedicaoDe(entradaSelecionada);
        Controle<Button>("UsarMenorPing").IsEnabled = escolhaManual && !conectando;
        Controle<Button>("Entrar").IsEnabled = !conectando && selecionada != null && selecionada.PodeConectar;
        bool temEndereco = entradaSelecionada != null;
        if (selecionada == null) legendaEntrada = "Escolha uma entrada na aba Servidor.";
        else if (selecionada.Medindo) legendaEntrada = "Medindo a " + entradaSelecionada.Nome + "…";
        else if (!selecionada.PodeConectar) legendaEntrada = entradaSelecionada.Nome + " indisponível. Atualize ou escolha outra.";
        else if (selecionada.Estado == null) legendaEntrada = entradaSelecionada.Nome + " selecionada · sem resposta ao teste";
        else legendaEntrada = entradaSelecionada.Nome + (escolhaManual ? " selecionada" : " · seleção automática") +
                (entradaSelecionada.UsaPonte ? " · " + selecionada.Transporte : "");
        if (!aviso.IsEnabled) Controle<TextBlock>("Mensagem").Text = legendaEntrada;
        var endereco = Controle<TextBlock>("Endereco");
        endereco.Text = temEndereco ? (selecionada != null && selecionada.UsaUdp ? "Conexão UDP direta" : entradaSelecionada.Endereco) : "";
        endereco.Visibility = temEndereco ? Visibility.Visible : Visibility.Collapsed;
        Controle<Button>("Copiar").Visibility = temEndereco && !entradaSelecionada.UsaPonte ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MostrarMedicao(MedicaoEntrada medicao, bool menorPing)
    {
        int numero = medicao.Entrada.Numero;
        bool precisaVinculo = medicao.Entrada.UsaPonte && AutorizacaoRota.PrecisaVinculo;
        if (medicao.Entrada.UsaPonte)
            Controle<Button>("VincularEntrada").Visibility = precisaVinculo ? Visibility.Visible : Visibility.Collapsed;
        string ping = medicao.Medindo ? "Medindo…" : medicao.Estado != null ? medicao.PingMs + " ms" :
            precisaVinculo ? "Aguardando liberação" : medicao.PodeConectar ? "Sem resposta" : "Indisponível";
        Controle<TextBlock>("PingEntrada" + numero).Text = ping;
        var destaque = Controle<TextBlock>("DestaqueEntrada" + numero);
        destaque.Text = menorPing ? "Menor ping" : "";
        destaque.Visibility = menorPing ? Visibility.Visible : Visibility.Collapsed;
        var opcao = Controle<RadioButton>("OpcaoEntrada" + numero);
        string resumo = medicao.Entrada.Nome + " · " + ping;
        if (medicao.Entrada.UsaPonte && medicao.Estado != null) resumo += " · " + medicao.Transporte;
        AutomationProperties.SetName(opcao, resumo + (menorPing ? " · Menor ping" : ""));
        opcao.ToolTip = medicao.Medindo ? resumo : resumo + " · " + medicao.Amostras + " de 3 respostas";
    }

    private async void VincularEntrada()
    {
        var botao = Controle<Button>("VincularEntrada");
        botao.IsEnabled = false;
        try
        {
            string texto = JanelaConvite.AvisoVinculo + "\n\nDeseja vincular este PC e liberar a Entrada 2?";
            if (MessageBox.Show(Janela, texto, "Liberar Entrada 2", MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var token = fecharCentral.Token;
            await Task.Run(() => AutorizacaoRota.VincularComConsentimento(token), token);
            if (token.IsCancellationRequested) return;
            botao.Visibility = Visibility.Collapsed;
            SondagemEntradas.RetomarEntrada2();
            Informar("Entrada 2 liberada para este PC.");
            Atualizar();
        }
        catch (Exception erro)
        {
            if (!CanalJogo.ErroDeRede(erro)) throw;
            if (!fecharCentral.IsCancellationRequested) Informar("Liberação pendente. Verifique a conexão e tente novamente.");
        }
        finally { botao.IsEnabled = true; }
    }

    private void MostrarEstado(ComparacaoEntradas rotas)
    {
        ultimasMedicoes = rotas;
        var menorPing = rotas.MenorPing;
        MostrarMedicao(rotas.Direta, rotas.Direta == menorPing);
        MostrarMedicao(rotas.Ponte, rotas.Ponte == menorPing);
        MostrarMedicao(rotas.Terceira, rotas.Terceira == menorPing);
        AtualizarEscolha();
        var info = rotas.Direta.Estado ?? rotas.Ponte.Estado ?? rotas.Terceira.Estado;
        if (info == null && (rotas.Direta.Medindo || rotas.Ponte.Medindo || rotas.Terceira.Medindo)) return;
        bool online = info != null;
        string cor = online ? "#8BE3AE" : "#E8BC77";
        var tinta = (Brush)new BrushConverter().ConvertFromString(cor);
        Controle<Ellipse>("PontoEstado").Fill = tinta;
        Controle<TextBlock>("TextoEstado").Foreground = tinta;
        Controle<TextBlock>("TextoEstado").Text = online ? "Online" : "Sem resposta";
        Controle<Border>("FundoEstado").Background = (Brush)new BrushConverter().ConvertFromString(online ? "#1D342B" : "#362D22");
        Controle<Border>("FundoEstado").ToolTip = (online ? "Resposta do servidor às " : "Sem resposta à consulta das ") +
            DateTime.Now.ToString("HH:mm:ss");
        Controle<TextBlock>("Mapa").Text = online ? NomeMapa(info.Mapa) : "—";
        Controle<TextBlock>("Mapa").ToolTip = online ? info.Mapa : null;
        var modo = Controle<TextBlock>("Modo");
        modo.Text = online && info.Modo != null ? info.Modo : "—";
        modo.ToolTip = online ? info.Modo ?? "O servidor ainda não anunciou um modo." : null;
        Controle<TextBlock>("Jogadores").Text = online ? info.Jogadores.ToString() : "—";
        Controle<TextBlock>("Jogadores").ToolTip = online ? "Bots informados pelo servidor: " + info.Bots : null;
        Controle<TextBlock>("Vagas").Text = online ? "/ " + info.Capacidade : "";
        AtualizarCenario(online ? info.Mapa : null);
        MostrarHistorico(info);
    }

    private void AtualizarCenario(string nomeMapa)
    {
        string recurso = Cenarios.Recurso(nomeMapa, recursos);
        if (recurso == imagemAtual) return;
        var cenario = Controle<Image>("Cenario");
        cenario.Source = Imagem(recurso);
        AutomationProperties.SetName(cenario, recurso == Cenarios.Padrao ? "Counter-Strike 2" : "Cenário de " + NomeMapa(recurso.Substring("mapa.".Length)));
        imagemAtual = recurso;
    }

    private static string NomeMapa(string original)
    {
        return Cenarios.Nome(original);
    }

    private async Task<string> SenhaParaEntrarAsync(EntradaMix entrada, CancellationToken token)
    {
        var senha = await Task.Run(() => AutorizacaoRota.ObterSenhaDoJogo(false, token), token);
        token.ThrowIfCancellationRequested();
        if (senha != null) return senha;

        var dialogo = new JanelaConvite(Janela, !entrada.UsaPonte);
        if (dialogo.ShowDialog() != true) return null;
        // Reconfira a licença após o diálogo, inclusive se o jogador digitou a senha.
        bool consentiu = dialogo.Vincular;
        senha = await Task.Run(() => AutorizacaoRota.ObterSenhaDoJogo(consentiu, token), token);
        token.ThrowIfCancellationRequested();
        if (senha == null) return dialogo.SenhaDigitada;
        SondagemEntradas.RetomarEntrada2();
        return senha;
    }

    private async void Entrar()
    {
        var escolha = entradaSelecionada;
        var medicao = sondagem;
        if (escolha == null || medicao == null || !Controle<Button>("Entrar").IsEnabled) return;
        var exibida = ultimasMedicoes.MedicaoDe(escolha);
        conectando = true;
        AtualizarEscolha();
        fecharAtualizador.Cancel();
        Controle<TextBlock>("TextoEntrar").Text = "Verificando entrada…";
        PonteJogo novaPonte = null;
        try
        {
            var rotas = await VerificarParaConectar(medicao, exibida);
            if (!Janela.IsVisible || fecharCentral.IsCancellationRequested) return;
            var rota = rotas.MedicaoDe(escolha);
            if (!rota.PodeConectar || (!escolhaManual && rota.Estado == null))
            {
                Informar(escolha.Nome + " indisponível. Atualize ou escolha outra.");
                return;
            }
            if (rota.UsaUdp != exibida.UsaUdp)
            {
                MostrarEstado(rotas);
                Informar("A Entrada 2 mudou para " + rota.Transporte + ". Confira o ping e clique em Entrar no mix novamente.");
                return;
            }
            var destino = rota.Destino;
            Controle<TextBlock>("TextoEntrar").Text = "Solicitando convite…";
            string senha = await SenhaParaEntrarAsync(escolha, fecharCentral.Token);
            if (senha == null || !Janela.IsVisible || fecharCentral.IsCancellationRequested) return;
            if (escolha.UsaPonte)
            {
                try
                {
                    if (ponte == null || ponte.Termino.IsCompleted || ponte.UsaUdp != rota.UsaUdp)
                    {
                        novaPonte = await PonteJogo.AbrirAsync(rota.Estado.SteamId, rota.UsaUdp);
                        ponte = novaPonte;
                    }
                    ponte.AguardarJogo();
                    destino = ponte.Endereco;
                }
                catch (Exception erro)
                {
                    if (!CanalWebSocket.ErroDeRede(erro)) throw;
                    Informar("A Entrada 2 não conectou. Tente de novo ou escolha outra.");
                    return;
                }
            }
            if (!Janela.IsVisible) return;
            Controle<TextBlock>("TextoEntrar").Text = "Abrindo Steam…";
            await Task.Run(() => Process.Start(Convite.PrepararConexao(destino, senha)));
            novaPonte = null; // A conexão do CS2 continua mesmo se a janela fechar.
            if (!Janela.IsVisible) return;
            Informar(rota.Entrada.UsaPonte ? rota.Entrada.Nome + " ativa. Pode fechar a janela." :
                (rota.Estado == null ? "Sem resposta ao teste. Tentando pela " + rota.Entrada.Nome + "." : "Conectando pela " + rota.Entrada.Nome + "."));
            await Task.Delay(1500);
        }
        catch (Win32Exception) { Informar("Confira se a Steam está instalada e tente novamente."); }
        catch (UnauthorizedAccessException) { Informar("Acesso ao convite recusado. Confira a licença do PC com o administrador."); }
        catch (Exception erro)
        {
            if (!CanalJogo.ErroDeRede(erro)) throw;
            if (!fecharCentral.IsCancellationRequested) Informar("A central não confirmou o convite. Tente novamente em instantes.");
        }
        finally
        {
            if (novaPonte != null) novaPonte.Parar();
            Controle<TextBlock>("TextoEntrar").Text = "Entrar no mix";
            conectando = false;
            AtualizarEscolha();
        }
    }

    private void CopiarEndereco()
    {
        if (entradaSelecionada != null && !entradaSelecionada.UsaPonte)
            Copiar(entradaSelecionada.Endereco, "Endereço copiado.");
    }

    private void Copiar(string texto, string mensagem)
    {
        try { Clipboard.SetText(texto); Informar(mensagem); }
        catch (ExternalException) { Informar("Falha ao copiar. Tente novamente."); }
    }

    private void Informar(string mensagem)
    {
        Controle<TextBlock>("Mensagem").Text = mensagem;
        aviso.Stop();
        aviso.Start();
    }
}
