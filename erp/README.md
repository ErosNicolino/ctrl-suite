# CTRL ERP — Gestão da Gráfica

Sistema de gestão para gráfica rápida: caixa, pedidos e produção, orçamentos, clientes, produtos, compras,
devedores e relatórios. Roda no Windows (desktop WPF) com um **servidor próprio** que também atende o
**celular** (PWA) pela rede local ou pelo Tailscale.

> Código publicado para portfólio. Dados, certificados e configurações do servidor real **não** fazem parte do repositório;
> telefones e nomes nos testes são fictícios.

## Módulos

| Área | O que faz |
|---|---|
| **Início** | Recebimentos do caixa por forma de pagamento, dinheiro esperado na gaveta, pedidos que pedem atenção e prontos para retirada. |
| **Caixa** | Abrir/fechar caixa, lançamentos, sangria, devolução, fechamento com Confere/Falta/Sobra em PDF. |
| **Pedidos e produção** | Pedidos agendados com etapas, sinal, prazo (regras de dias úteis e feriados de SP), ficha do pedido em PDF com QR, terceirizados e retrabalho. |
| **Orçamentos** | Prévia e conversão em pedido. |
| **Clientes e devedores** | Cadastro sem duplicar telefone, mesclagem, cobrança com modelos de mensagem e WhatsApp. |
| **Produtos, compras e fornecedores** | Catálogo com custo e preço sugerido, necessidades de compra. |
| **Relatórios** | Financeiro por período, custo e lucro por pedido (só pedidos conferidos). |
| **Sistema** | Usuários e permissões, segurança, backups automáticos, aparência (claro/escuro, cores Viva/Sóbria, tamanho do texto). |
| **Celular (PWA)** | Início, pedidos, produção, calculadora e lançamento de venda, com pareamento seguro do aparelho. |
| **Acompanhamento** | Página pública para o cliente acompanhar o pedido pelo link. |

## Arquitetura

```
Wpf/          Desktop (.NET 10, WPF) — telas, componentes UX 2.0, tema
Core/         Núcleo compartilhado — dados (SQLite), regras, servidor web, relatórios em PDF, segurança
ServerHost/   Serviço do Windows que hospeda a API e a PWA (HTTPS na LAN e no Tailscale)
Pwa/          App de celular (HTML/CSS/JS, service worker)
Instalacao/   Scripts de pacote e atualização
*Tests/       Integridade, API web, hotfix, desempenho e assíncronos
UxCapture/    Gera capturas das telas (claro/escuro) a partir de uma cópia do banco
docs/         Padrão de UX/UI (ui-padrao.md)
```

- **Gravações validadas no servidor:** o desktop envia comandos que passam por permissão e controle de versão (`Core/Servidor/BusinessWrites.cs`).
- **Segurança:** dispositivos pareados por código temporário, cookies protegidos, API só com Bearer/dispositivo (sem CORS curinga), senhas com hash.
- **Design system:** cores e componentes em `Wpf/Interface/ThemeManager.cs` e `Wpf/Interface/Componentes/` — o mesmo padrão usado pelo CTRL 3D e pelo CTRL Ponto.

## Como compilar e testar

```bash
dotnet build ServerHost/GraficaCaixa.ServerHost.csproj
dotnet build Wpf/GraficaCaixa.Wpf.csproj
dotnet run --project IntegrityTests/IntegrityTests.csproj
```

Ou tudo de uma vez: `powershell -ExecutionPolicy Bypass -File Rodar_Testes.ps1`.
