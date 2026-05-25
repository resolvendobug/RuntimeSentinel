# RuntimeSentinel

![.NET](https://img.shields.io/badge/.NET-netstandard2.0+-512BD4?logo=dotnet)
![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-green)
![Testes](https://img.shields.io/badge/testes-36%20passando-brightgreen)

Versao em ingles: [README.md](README.md)

RuntimeSentinel e um toolkit open source de analyzers Roslyn focado em **estabilidade operacional** para aplicacoes .NET.

Ele foi criado para responder, ainda durante o desenvolvimento, perguntas reais de producao, como:

- Esse codigo pode saturar dependencias sob alta carga?
- Esse fluxo async pode bloquear threads e reduzir throughput?
- Esse padrao pode aumentar pressao de memoria e GC?
- Essa estrategia de retry pode amplificar falhas em vez de conter?

## Por Que Essa Lib Foi Feita

A maioria dos analyzers estaticos cobre bem estilo e corretude. Riscos operacionais — problemas de concorrencia, tempestade de retries, pressao de memoria sob carga — costumam aparecer tarde: em testes de carga ou incidentes em producao.

O RuntimeSentinel antecipa essa deteccao tornando riscos de alto impacto em tempo de execucao visiveis **em tempo de compilacao**, no IDE e em pipelines de CI.

Foco em:

- Estabilidade sob carga
- Concorrencia segura
- Throughput previsivel
- Eficiencia de memoria
- Padroes de comunicacao resilientes

## Regras Implementadas

| Regra | Categoria | O que analisa | Por que importa |
|---|---|---|---|
| RS1001 | Concurrency | `Thread.Sleep` dentro de metodo `async` | Bloqueia threads reais; sob carga isso satura o thread pool |
| RS1002 | Concurrency | `Task.WhenAll` com fan-out sem limite explicito | Fan-out sem bound pode inundar dependencias downstream |
| RS1003 | Concurrency | `Parallel.ForEach` sem `MaxDegreeOfParallelism` | Pode fazer oversubscribe de CPU e saturar recursos compartilhados |
| RS1004 | Async | Chamadas bloqueantes `.Wait()` / `.Result` em contexto async | Bloqueia threads e eleva risco de deadlock/starvation |
| RS1005 | Memory | `ToList()` em query sem limite explicito | Pode gerar pico de memoria e acionar GC sob carga |
| RS1006 | Communication | `new HttpClient()` criado dentro de metodo | Instanciacao por chamada causa esgotamento de sockets ao longo do tempo |
| RS1007 | Communication | Chamada HTTP sem timeout explicito ou `CancellationToken` | Requisicoes podem ficar presas indefinidamente em picos de latencia |
| RS1008 | Communication | Loop de retry sem limite maximo de tentativas | Retries infinitos amplificam carga sobre dependencia ja falhando |
| RS1009 | Communication | Retry com atraso fixo em vez de backoff exponencial | Intervalo fixo mantem requisicoes intensas sobre dependencia instavel |
| RS1010 | Communication | Backoff exponencial sem jitter | Retries sincronizados entre instancias geram rajadas coordenadas |

## Code Fix — RS1002

Para RS1002 (`Task.WhenAll` com fan-out ilimitado), o RuntimeSentinel inclui um code fix automatico com duas estrategias:

**Antes:**
```csharp
var results = await Task.WhenAll(items.Select(i => ProcessAsync(i)));
```

**Depois — opcao 1: limitar com `Take`:**
```csharp
var results = await Task.WhenAll(items.Take(100).Select(i => ProcessAsync(i)));
```

**Depois — opcao 2: limitar concorrencia com `SemaphoreSlim`:**
```csharp
var semaphore = new SemaphoreSlim(10);
var results = await Task.WhenAll(items.Select(async i =>
{
    await semaphore.WaitAsync();
    try { return await ProcessAsync(i); }
    finally { semaphore.Release(); }
}));
```

## Score de Risco Operacional

Alem dos diagnosticos individuais, o RuntimeSentinel inclui um motor de pontuacao que produz um **relatorio composto de risco operacional** para um trecho de codigo C#.

```csharp
var scorer = new OperationalRiskScorer();
var report = await scorer.AnalyzeSourceAsync(sourceCode);

Console.WriteLine(report.OperationalRisk);      // 0-100
Console.WriteLine(report.OperationalRiskLevel);  // Low / Medium / High
Console.WriteLine(report.ToMarkdown());
Console.WriteLine(report.ToJson());
```

O relatorio inclui:

| Campo | Descricao |
|---|---|
| `ConcurrencyRisk` | Score 0-100 para problemas de concorrencia |
| `AsyncRisk` | Score 0-100 para bloqueios async |
| `MemoryRisk` | Score 0-100 para pressao de memoria |
| `OperationalRisk` | Score composto ponderado (0-100) |
| `OperationalRiskLevel` | Classificacao `Low` / `Medium` / `High` |
| `Findings` | Lista de diagnosticos individuais com localizacao |
| `Justifications` | Explicacoes legíveis por humanos para cada finding |

Pesos padrao: Concorrencia `50%` · Async `30%` · Memoria `20%`.  
Limiares: `Low < 30` · `30 ≤ Medium < 70` · `High >= 70`.

Pesos e limiares sao configuráveis via `OperationalRiskScoringOptions`.

## Inicio Rapido

1. Clone e build:

```bash
git clone https://github.com/your-org/RuntimeSentinel
dotnet build RuntimeSentinel.slnx
```

2. Executar todos os testes:

```bash
dotnet test test/RuntimeSentinel.Analyzers.Tests/RuntimeSentinel.Analyzers.Tests.csproj
```

3. Referencie o pacote NuGet no seu projeto (apos publicacao).

## Compatibilidade

- Target framework: `netstandard2.0`
- Compativel com .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5/6/7/8/9/10+
- Integra com qualquer IDE baseado em Roslyn (Visual Studio, VS Code com C# Dev Kit, Rider)

## Estrutura do Repositorio

```
src/
  RuntimeSentinel.Analyzers/    Analyzers Roslyn (RS1001-RS1010)
  RuntimeSentinel.CodeFixes/    Code fixes automaticos para regras selecionadas
  RuntimeSentinel.Scoring/      Motor de score de risco operacional
test/
  RuntimeSentinel.Analyzers.Tests/  Suite de testes completa (36 testes)
samples/
  RuntimeSentinel.SampleApp/    Laboratorios praticos com exemplos das regras
```

## Escopo

O RuntimeSentinel foca em analyzers e primitivas de score operacional para C#/.NET.

Fora de escopo na v1:

- Lint generico de estilo ou convencoes de nomenclatura
- Substituir testes de carga ou monitoramento em tempo de execucao
- Funcionalidades completas de plataforma de observabilidade

## Contribuindo

Contribuicoes sao bem-vindas. Abra uma issue antes de enviar um pull request para discutir a mudanca proposta.

## Licenca

Este projeto usa a licenca MIT. Veja [LICENSE](LICENSE).
