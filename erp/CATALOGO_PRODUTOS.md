# Produtos e custos — 22/09/2026

Fonte única: Cadastro de Produtos, persistido no SQLite. O catálogo não usa mais a importação antiga embutida.

Excel futuro: **Descrição | Categoria | Unidade | Custo | Preço de Venda**.
Não criar IDs ou códigos: serão gerados internamente. Uma linha por produto/serviço; custo e preço numéricos não negativos. Categoria e unidade podem ficar vazias. Nenhuma importação foi feita nesta rodada.

Produtos ativos aparecem na busca por palavras (sem diferenciar acentos/caixa). Preços e custos sugeridos são copiados para o pedido e continuam editáveis. Produto usado no histórico pode ser inativado, mas não excluído.

Cada item mantém custo unitário e revisão. Conferido exige todos os itens revisados, inclusive custo zero; Pendente e Ignorado não geram lucro apurado. Custo total = itens + frete + adicionais. Lucro = venda final após desconto − custo total. O campo de custo global foi retirado em 23/09/2026; os valores dos pedidos 7 e 19 foram exportados em Backups/Antes-fotos-custos-20260923/custos-globais-antigos.json para revisão manual.

A aba Custo salva somente custos; pedidos concluídos exigem justificativa. Auditoria preserva usuário, momento, valores anteriores e novos. Relatórios seguem o período pela data de criação já adotada; custos entram somente para pedidos concluídos e conferidos. Custos faltantes sinalizam resultado parcial. Despesas e lucro líquido não apurados, pois não existe fonte de despesas operacionais. Sangrias não são convertidas em despesas.

Atualize o computador principal e os clientes para a mesma versão antes de novas gravações. Clientes antigos são bloqueados para impedir perda dos novos campos.
