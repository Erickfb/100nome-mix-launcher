# Leitura do placar de um servidor CS2

Esta amostra usa duas fontes do launcher 100Nome Mix. `ConsultaServidor.cs` interpreta a resposta A2S_INFO do jogo e as tags de modo e placar. `Cenarios.cs` identifica os mapas pelo nome ou pelo item do Workshop. Os identificadores no catálogo são públicos.

`Exemplo.cs` monta uma partida fictícia na memória e passa a resposta ao mesmo leitor usado pelo launcher. O exemplo funciona offline e dispensa Steam, licença de PC, credenciais e arquivos do jogo.

## Compilar e conferir a amostra

Na pasta exportada, abra o Windows PowerShell 5.1 com o .NET Framework 4.x instalado e execute:

```powershell
.\Testar-Exemplo.ps1
```

O teste compila em uma pasta temporária, valida os dados da partida e remove o executável após a execução. O resultado esperado é `Treino | Competitivo | 3 pessoas e 1 bot | Placar da amostra: 13 x 10`.

## Licença e escopo

O código e o exemplo deste repositório são distribuídos sob a [licença MIT](LICENSE). Este é o primeiro componente aberto do 100Nome Mix. A distribuição do aplicativo completo para Windows segue o canal do grupo. O teste deste repositório usa exclusivamente a partida fictícia descrita acima.
