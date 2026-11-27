# Launcher de comunidade para CS2

O cliente para Windows consulta um servidor de Counter-Strike 2, compara duas entradas diretas, mostra o guia de comandos e abre a Steam para conectar. A distribuição pública usa WPF e inclui uma demonstração offline. Você pode compilar as fontes com os serviços da sua comunidade.

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

O segundo teste monta uma resposta A2S em memória e confirma três pessoas, um bot e o placar de 13 × 10. A cada revisão na branch principal, o GitHub Actions compila as duas configurações em um Windows temporário e verifica os controles WPF e a amostra. [Verificação no Windows](.github/workflows/validar-windows.yml)

## Usar com uma comunidade

Copie `perfil.exemplo.json` para uma pasta de trabalho local. Preencha os dois domínios de jogo, a porta HTTPS do cadastro, o SteamID público do dedicado e o link HTTPS da comunidade. O IPv4 do serviço de cadastro é opcional. Use nomes próprios para a pasta de dados e para o contêiner da chave do Windows. A compilação usa a pasta e o prefixo de chaves `MixComunidades` para isolar esses dados de outras aplicações. Informe em base64 o SHA-256 da chave pública do certificado TLS do cadastro e o módulo RSA público do emissor de licenças. Compartilhe arquivos de perfil somente com as pessoas que precisam dessa configuração.

```powershell
.\Compilar.ps1 -Perfil ..\minha-comunidade.json -Destino '.\Minha comunidade.exe'
powershell.exe -NoProfile -STA -File .\Testar-Interface.ps1 -Exe '.\Minha comunidade.exe'
```

Esse build abre a tela de ativação. Um PC aprovado pela sua central pode abrir a aba **Servidor**, comparar as Entradas 1 e 3 ou fixar uma delas e clicar em **Entrar no mix**. A ativação precisa de uma central que implemente o protocolo do cliente e de um certificado que corresponda ao pin do perfil. Os dados públicos usados no build ficam legíveis no executável.

As compilações geradas por este repositório usam atualização manual. O código do atualizador está disponível para revisão. O pacote de recursos da distribuição privada do grupo tem um ciclo próprio. O nome do projeto identifica a origem do código. A demonstração e as compilações de comunidade usam os elementos visuais do próprio XAML e as fontes disponíveis no Windows.

## Dados usados pelo cliente configurado

Ao abrir, o cliente calcula um código do PC a partir da instalação do Windows e de uma chave de assinatura do perfil. Com o cadastro automático aberto, pode enviar à central esse código e o apelido público da conta Steam ativa. A central verifica a licença a cada abertura. Vincular o PC para os convites requer aceite no launcher. Nesse momento, o cliente envia pelo HTTPS da central o identificador de instalação do Windows. As conexões seguintes comprovam a posse da chave com uma assinatura. Cada comunidade é responsável pelo tratamento dos dados recebidos.

A aba **Servidor** consulta o CS2 diretamente por UDP nas duas entradas configuradas. **Notícias** consulta o feed da Steam e mantém uma cópia local para leitura. **Estatísticas** pode consultar a patente da conta Steam ativa. O botão de conexão entrega o convite ao aplicativo Steam. A licença e o cache usam a pasta local definida no perfil.

## Licenças

As fontes deste checkout estão sob a licença MIT. [Licença do projeto](LICENSE)
