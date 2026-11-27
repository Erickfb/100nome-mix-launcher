using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

internal sealed class JanelaAtivacao : Window
{
    private readonly string codigo;
    private readonly TextBlock retorno = new TextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel opcoes = new StackPanel { Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource fechar = new CancellationTokenSource();
    private readonly Button verificar = Botao("Verificar cadastro", "#7842BC");
    private readonly Button importar = Botao("Importar autorização", "#35313D");
    private bool verificouAtualizacao;

    private static Brush Cor(string valor) { return (Brush)new BrushConverter().ConvertFromString(valor); }

    internal JanelaAtivacao(string codigoPC)
    {
        codigo = codigoPC;
        Title = "Acesso ao 100Nome Mix";
        Width = Math.Min(510, SystemParameters.WorkArea.Width);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Background = Cor("#17151D"); Foreground = Brushes.White;
        using (var imagem = typeof(JanelaAtivacao).Assembly.GetManifestResourceStream("icone"))
            if (imagem != null) Icon = BitmapFrame.Create(imagem, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var conteudo = new StackPanel { Margin = new Thickness(26) };
        Content = conteudo;
        conteudo.Children.Add(new TextBlock { Text = "Acesso ao 100Nome Mix", FontSize = 26, FontWeight = FontWeights.SemiBold });
        retorno.Foreground = Cor("#DFCAFF"); retorno.Margin = new Thickness(0,12,0,0);
        conteudo.Children.Add(retorno);
        conteudo.Children.Add(opcoes);
        opcoes.Children.Add(new TextBlock { Text = "Código deste PC", Margin = new Thickness(0,15,0,5) });
        var campo = new TextBox { Text = codigo, IsReadOnly = true, Padding = new Thickness(9), FontSize = 14,
            Background = Cor("#24212E"), Foreground = Brushes.White, BorderBrush = Cor("#50495E") };
        System.Windows.Automation.AutomationProperties.SetName(campo, "Código deste PC");
        opcoes.Children.Add(campo);
        var botoes = new WrapPanel { Margin = new Thickness(0,15,0,0) };
        var copiar = Botao("Copiar código", "#35313D");
        copiar.Click += delegate {
            try { Clipboard.SetText(codigo); retorno.Text = "Código copiado."; }
            catch (ExternalException) { campo.Focus(); campo.SelectAll(); retorno.Text = "Use Ctrl+C para copiar o código."; }
        };
        importar.Click += delegate {
            var escolha = new OpenFileDialog { Filter = "Autorização do mix (*.100nome)|*.100nome", CheckFileExists = true };
            if (escolha.ShowDialog(this) != true) return;
            try { LicencaPC.Importar(escolha.FileName, codigo); ConsultarCadastro(); }
            catch (InvalidDataException erro) { retorno.Text = erro.Message; }
            catch (Exception)
            { retorno.Text = "Não foi possível validar e salvar essa autorização. Confira o arquivo recebido."; }
        };
        verificar.Click += delegate { ConsultarCadastro(); };
        botoes.Children.Add(verificar); botoes.Children.Add(copiar); botoes.Children.Add(importar); opcoes.Children.Add(botoes);
        Loaded += delegate {
            // O atualizador precisa reconhecer a tela de ativação como inicialização bem-sucedida.
            Atualizacao.SinalizarInterfacePronta();
            ConsultarCadastro();
        };
        Closed += delegate { fechar.Cancel(); };
    }

    private async void ConsultarCadastro()
    {
        if (!verificar.IsEnabled || fechar.IsCancellationRequested) return;
        verificar.IsEnabled = importar.IsEnabled = false;
        retorno.Text = "Consultando a licença deste PC no servidor…";
        try
        {
            var estado = await CadastroAutomatico.ConferirAcesso(codigo, fechar.Token);
            if (fechar.IsCancellationRequested) return;
            if (estado == CadastroAutomatico.Resultado.Autorizado) { DialogResult = true; return; }
            switch (estado)
            {
                case CadastroAutomatico.Resultado.Revogado:
                    retorno.Text = "O administrador revogou a licença deste PC. Envie o código abaixo para pedir a reativação.";
                    break;
                case CadastroAutomatico.Resultado.Fechado:
                    retorno.Text = "O cadastro está fechado. Envie o código abaixo ao administrador. Depois que ele autorizar este PC, clique em Verificar cadastro.";
                    break;
                case CadastroAutomatico.Resultado.MuitasTentativas:
                    retorno.Text = "Muitos pedidos. Aguarde um minuto e clique em Verificar cadastro.";
                    break;
                default:
                    retorno.Text = "O servidor não confirmou a licença deste PC. Confira sua conexão e clique em Verificar cadastro para tentar novamente.";
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!fechar.IsCancellationRequested) retorno.Text = "Falha ao verificar a licença. Tente novamente ou confira com o administrador se este PC está autorizado."; }
        finally
        {
            if (!fechar.IsCancellationRequested)
            {
                opcoes.Visibility = Visibility.Visible;
                verificar.IsEnabled = importar.IsEnabled = true;
                VerificarAtualizacao();
            }
        }
    }

    private async void VerificarAtualizacao()
    {
        if (verificouAtualizacao) return;
        verificouAtualizacao = true;
        try
        {
            if (await Atualizacao.VerificarAsync(fechar.Token) && !fechar.IsCancellationRequested && Atualizacao.AtivarPendente())
                DialogResult = false;
        }
        catch (Exception) { /* O botão de verificar cadastro continua disponível. */ }
    }

    private static Button Botao(string texto, string fundo)
    {
        return new Button { Content = texto, Padding = new Thickness(14,8,14,8),
            Margin = new Thickness(0,0,8,0), Background = Cor(fundo), Foreground = Brushes.White,
            BorderBrush = Cor("#726680"), Cursor = System.Windows.Input.Cursors.Hand };
    }
}
