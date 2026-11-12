# Instruções do projeto

- Padrão de UX/UI (tokens, componentes, tabelas, badges, botões, modais, estados, toasts, PDFs e celular): siga [docs/ui-padrao.md](docs/ui-padrao.md) antes de criar ou alterar telas.

## Pastas
- Desenvolvimento: `D:\ERP_EDIT\Codigo-fonte` (este repositório). Pacotes gerados: `D:\ERP_EDIT\Pacotes`.
- Produção (não editar direto): `D:\Grafica CA CP\ERP` — programa em `Programa\`, serviço Windows `GraficaCaixaServer`, banco em `Dados\caixa_dados.db`.
- Referências visuais (prints, mascote, logos): `D:\ERP_EDIT\Referencias`.

## Como o usuário trabalha
- Respostas curtas em português; relatórios em tabela; plano antes de codar quando a mudança é grande.
- **Mudança de segurança ou de regra de negócio: perguntar antes.** Se algo citado não for encontrado, perguntar em vez de adivinhar.
- Ao remover algo, remover também o código morto (handlers, imports, rotas), **sem mexer em dados do banco**.
- Celular: mudanças só nos breakpoints mobile (`max-width:1099px`); zero regressão no desktop.
- Build e testes ao fim de cada bloco. Não instalar em produção: gerar o pacote e o usuário publica.

## Stack e regras técnicas
- .NET 10 WPF (`Wpf/`), servidor `ServerHost/` (serviço), núcleo `Core/` (arquivo novo compartilhado precisa entrar no `ServerHost/GraficaCaixa.ServerHost.csproj`).
- PWA em `Pwa/` (servida em `/app/`, embutida no servidor): arquivo novo entra em `BrowserGateway.AssetTypes` e no `SHELL` do `sw.js`.
- Configurações compartilhadas ficam no banco (tabela `metadata`): `company_profile_v1`, `backup_schedule_v1`, `calculator_settings_v1`, `business_rules_v1` (Sistema › Configurações › Regras: prazos, feriados de SP, sinal, devedores, orçamento).
- Gravações do desktop passam por comandos validados no servidor (`Core/Servidor/BusinessWrites.cs`, `BusinessUi.ExecuteAsync`), com permissão e controle de versão.
- Sem Python na máquina. Node.js instalado (09/11/2026) só para o Claude Code e plugins; scripts do projeto continuam em PowerShell/perl. Arquivos `.cmd` precisam de CRLF; `.ps1` com acentos precisam de UTF-8 com BOM.

## Versão (subir nos 5 lugares antes de gerar pacote)
`Wpf/GraficaCaixa.Wpf.csproj`, `ServerHost/GraficaCaixa.ServerHost.csproj`, `Core/Servidor/BrowserGateway.cs` (`Version`), `Pwa/app.js` e `Pwa/sw.js` (`VERSION`). Formato `AAAA.MM.DD.N`.

## Build e testes
Tudo de uma vez (builds, todas as suítes, API e hotfix, com resumo no fim): `powershell -ExecutionPolicy Bypass -File Rodar_Testes.ps1` (`-Filtro <parte do nome>` para só algumas).
```
dotnet build ServerHost/GraficaCaixa.ServerHost.csproj
dotnet build Wpf/GraficaCaixa.Wpf.csproj
dotnet run --project IntegrityTests/IntegrityTests.csproj
dotnet run --project WpfAsyncTests/WpfAsyncTests.csproj
dotnet run --project WebApiTests/WebApiTests.csproj -- <pasta-vazia> ServerHost/bin/Debug/net10.0-windows/win-x64/GraficaCaixaServer.exe
dotnet run --project HotfixTests/HotfixTests.csproj -- <pasta-vazia>
```
- Telas do desktop com dados reais (cópia só leitura do banco): `dotnet run --project UxCapture/UxCapture.csproj -- --saida <pasta> --banco "D:\Grafica CA CP\ERP\Dados\caixa_dados.db" --combos claro-1 [--so <prefixo>]`.

## Pacote e publicação
1. Escrever as notas da versão (O_QUE_MUDOU) num arquivo.
2. `powershell -ExecutionPolicy Bypass -File Instalacao\Gerar_Pacote.ps1 -Notas <arquivo>` → `D:\ERP_EDIT\Pacotes\pacote-<versão>` (confere a versão nos 5 lugares e gera `pacote.sha256`).
3. O usuário coloca o pacote em `D:\Grafica CA CP\ERP\Atualizacoes\` e clica em **Atualizar ERP** (`Instalacao/Atualizar_ERP.cmd`, copiado uma vez para essa pasta). Nada é agendado: só atualiza quando ele clica e confirma.
