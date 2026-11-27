# Launcher de comunidade para CS2

O cliente para Windows consulta um servidor de Counter-Strike 2, compara duas entradas diretas, mostra o guia de comandos e abre a Steam para conectar. A distribuição pública usa WPF e inclui uma demonstração offline. Você pode compilar as fontes com os serviços da sua comunidade.

## Baixar e usar o 100Nome Mix

Baixe o arquivo `100Nome Mix.exe` na página de versões ou receba a mesma cópia da administração do grupo. Abra o arquivo no seu perfil normal do Windows. Em um PC novo, leia o aviso e envie o código exibido à administração. Depois da aprovação, clique em **Verificar cadastro**. A central entrega as duas entradas do servidor, o mural, o guia de comandos e as imagens. Para jogar, abra a Steam, escolha a entrada e clique em **Entrar no mix**. [Versões para Windows](https://github.com/Erickfb/100nome-mix-launcher/releases)

O GitHub Actions gera o EXE portátil em Windows usando o código desta revisão. O arquivo `cadastro.json` indica o serviço HTTPS e a impressão da chave pública para verificar a conexão. A pasta de dados, a identidade do servidor de jogo e o canal de atualizações são fornecidos pela central. Para repetir a compilação, use Windows PowerShell 5.1 e o .NET Framework 4.8.

```powershell
.\Compilar.ps1 -Bootstrap .\cadastro.json -Edicao ([long](Get-Content .\edicao.txt)) -Destino '.\100Nome Mix.exe'
```

## Abrir o guia offline

Abra o Windows PowerShell 5.1 no checkout, em um Windows com .NET Framework 4.8, e execute os comandos abaixo.

```powershell
.\Compilar.ps1
& '.\Mix demonstracao.exe'
```

O executável abre o guia de comandos ilustrativo. Pesquise por `ready`, selecione uma categoria ou copie um comando. O catálogo traz `/ajuda`, `!ready` e `!rank`. Edite `GuiaPublico.cs` para documentar os comandos da sua comunidade. A demonstração roda sem Steam, tráfego de rede, cadastro e acesso às chaves do perfil.

```powershell
powershell.exe -NoProfile -STA -File .\Testar-Interface.ps1 -Exe '.\Mix demonstracao.exe'
.\Testar-Exemplo.ps1
```

O segundo teste monta uma resposta A2S em memória e confirma três pessoas, um bot e o placar de 13 × 10. A cada revisão na branch principal, o GitHub Actions compila as três configurações em um Windows temporário e verifica os controles WPF e a amostra. [Verificação no Windows](.github/workflows/validar-windows.yml)

## Usar com uma comunidade

Copie `perfil.exemplo.json` para uma pasta de trabalho local. Preencha os dois domínios de jogo, a porta HTTPS do cadastro, o SteamID público do dedicado e o link HTTPS da comunidade. O IPv4 do serviço de cadastro é opcional. Use nomes próprios para a pasta de dados e para o contêiner da chave do Windows. A compilação usa a pasta e o prefixo de chaves `MixComunidades` para isolar esses dados de outras aplicações. Informe em base64 o SHA-256 da chave pública do certificado TLS do cadastro e o módulo RSA público do emissor de licenças. Compartilhe arquivos de perfil somente com as pessoas que precisam dessa configuração.

```powershell
.\Compilar.ps1 -Perfil ..\minha-comunidade.json -Destino '.\Minha comunidade.exe'
powershell.exe -NoProfile -STA -File .\Testar-Interface.ps1 -Exe '.\Minha comunidade.exe'
```

Esse build abre a tela de ativação. Um PC aprovado pela sua central pode abrir a aba **Servidor**, comparar as Entradas 1 e 3 ou fixar uma delas e clicar em **Entrar no mix**. A ativação precisa de uma central que implemente o protocolo do cliente e de um certificado que corresponda ao pin do perfil. Os dados públicos usados no build ficam legíveis no executável.

Os builds de demonstração e de comunidade com perfil próprio usam atualização manual. No EXE do grupo, o atualizador verifica o manifesto assinado, o tamanho e o SHA-256 do candidato antes de substituir o arquivo. O nome do projeto identifica a origem do código. A demonstração e as compilações de comunidade usam os elementos visuais do próprio XAML e as fontes disponíveis no Windows.

## Privacidade

Na primeira abertura do EXE do grupo em um PC novo, você pode prosseguir com o cadastro ou fechar o aplicativo. A consulta inicial à central usa HTTPS com verificação da sua chave pública. Para autorizar um PC, a central recebe o código da instalação e, quando disponível no cadastro, o apelido público da conta Steam ativa. O Windows mantém a chave privada do PC no perfil do usuário. No primeiro vínculo, o launcher pede autorização para enviar o identificador da instalação do Windows, usado para conferir o cadastro. A central guarda a impressão da chave pública para verificar as próximas assinaturas.

Ao abrir, o launcher consulta o estado da licença. Um PC aprovado pode carregar o conteúdo do grupo e verificar o canal de atualizações no GitHub. A aba **Servidor** mede o ping por UDP nas duas entradas. A aba **Estatísticas** pode consultar a patente usando o SteamID da conta ativa. **Notícias** pode consultar o feed da Valve e salvar uma cópia local. **Entrar no mix** solicita o convite e entrega o endereço e a senha à Steam. **Discord do mix** abre o link do grupo. Os serviços de rede recebem o IP da conexão. [Privacidade do GitHub](https://docs.github.com/pt/site-policy/privacy-policies/github-general-privacy-statement) · [Privacidade da Valve](https://store.steampowered.com/privacy_agreement/) · [Privacidade do Discord](https://discord.com/privacy)

Para remover o programa, feche a janela e exclua o EXE e a cópia de recuperação, se houver. A licença e os dados locais pertencem ao perfil do Windows. O administrador do grupo pode revogar ou excluir um cadastro pelo painel e receber pedidos referentes aos dados no canal do mix. A demonstração offline pode ser usada para experimentar a interface.

## Code signing policy

O projeto está preparando a candidatura à assinatura gratuita pela [SignPath.io](https://about.signpath.io/), com certificado da [SignPath Foundation](https://signpath.org/). As versões 1.22.0 e 1.22.1 são distribuídas sem assinatura de editor do Windows.

O mantenedor do repositório revisa as fontes e o workflow de compilação, confere o hash do artefato gerado pelo GitHub e autoriza a distribuição. Cada release identifica a versão e o SHA-256 do EXE. Enquanto o projeto aguarda uma assinatura de editor para Windows, o status do EXE é informado nas notas da versão. O manifesto assinado do auto-update é verificado pelo launcher. [Workflow de compilação](.github/workflows/validar-windows.yml) · [Mantenedor](https://github.com/Erickfb)

## Licenças

As fontes deste checkout estão sob a licença MIT. [Licença do projeto](LICENSE)
