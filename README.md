# CsvToPdf

Transforme arquivos CSV em PDFs com tabela organizada, direto pelo terminal.

## Início rápido

Você precisa do Windows e do [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/eltonmobarbosa/csv-to-pdf.git
cd csv-to-pdf
dotnet run --project src/CsvToPdf -- samples/vendas.csv saida.pdf
```

O arquivo `saida.pdf` será criado na pasta do projeto.

## Como usar

```text
dotnet run --project src/CsvToPdf -- <entrada.csv> [saida.pdf] [--force]
```

| Argumento | Obrigatório | O que faz |
| --- | --- | --- |
| `entrada.csv` | Sim | Arquivo CSV que será convertido |
| `saida.pdf` | Não | Caminho do PDF. Sem ele, o PDF fica ao lado do CSV |
| `--force` | Não | Substitui o PDF se ele já existir |

## O que o conversor faz

- **Separador automático:** reconhece vírgula, ponto e vírgula, tabulação e barra vertical.
- **Acentos corretos:** lê UTF-8, UTF-16 e Windows-1252, formato comum do Excel em português.
- **Campos complexos:** aceita aspas e quebras de linha dentro das células.
- **PDF pronto para leitura:** usa A4 paisagem, repete o cabeçalho e numera as páginas.

## Limites

- O documento pode ter até **25 colunas**.
- Cada registro precisa caber em **uma página**. Se não couber, a conversão para com uma mensagem de erro.
- O programa roda apenas no **Windows**, pois usa a fonte Arial instalada no sistema.

## Estrutura do projeto

```text
src/CsvToPdf/
  Program.cs       Lê os argumentos e trata os erros
  CsvLoader.cs     Lê e interpreta o CSV
  PdfExporter.cs   Monta e salva o PDF
tests/             Testes automatizados
samples/           CSV e PDF de exemplo
```

## Testes

```powershell
dotnet test CsvToPdf.slnx
```

## Dependências

O projeto usa `PDFsharp-MigraDoc` fixado na versão **6.2.4**, a última sob licença MIT.

> **Atenção:** a partir da versão 6.5.0, o pacote usa AGPL-3.0 e exige licença em tempo de execução. Avalie a licença antes de atualizar.
