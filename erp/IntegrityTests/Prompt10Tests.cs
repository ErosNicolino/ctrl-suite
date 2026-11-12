using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Windows.Controls;

static class Prompt10Tests
{
    static int passed;static readonly OperationContext Admin=OperationContext.Administrator("Prompt 10");
    static void Check(bool value,string name){if(!value)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var products=new List<Product>{new(){Description="Chaveiro com foto",Category=""},new(){Description="Caixinha para caneca",Category=""},new(){Description="Produto sem pista",Category=""},new(){Description="Banner",Category="Categoria preservada"}};
        Check(ProductOperations.FillMissingCategories(products)==3,"preenche somente categorias vazias");Check(products[0].Category=="Brindes Personalizados"&&products[1].Category=="Embalagens, Pastas e Envelopes"&&products[2].Category=="Revisar"&&products[3].Category=="Categoria preservada","prioridade de contexto e preservação");
        var search=new ProductSearch([new Product{Description="Cartão de visita com verniz",Category="Impressos Comerciais"},new Product{Description="Cartão simples",Category="Impressos Comerciais"}]);Check(search.Find("cartão verniz",category:"Impressos Comerciais").Single().Description.Contains("verniz"),"pesquisa por palavras separadas e categoria");
        var q=new TextBox{Text="9"};var p=new TextBox();var sub=new TextBox();var hint=new TextBlock();var binding=new QuantityTierBinding(q,p,sub,hint,()=>false,()=>{});var exact=new Product{ExactQuantityPrices=[new ExactQuantityPrice{Quantity=5,TotalPrice=7}]};binding.Choose(exact);var total=ItemPricing.Update(q,p,sub,false,out var valid);Check(valid&&q.Text=="5"&&total==7&&sub.Text=="7,00","uma opção exata é autoselecionada");Check(binding.ExactChoices.Visibility==System.Windows.Visibility.Visible&&new ExactQuantityChoice(1000,700).Label.Contains("1.000 un.")&&!new ExactQuantityChoice(1,35).Label.Contains("ExactQuantityChoice"),"opção exata exibe apenas o rótulo");
        var cost=new OrderItem{Quantity=1000,UnitCost=.095m,EnteredTotalCost=95m,CostReviewed=true};Check(cost.TotalCost==95m,"custo total exato preservado");var derived=new OrderItem{Quantity=1000,UnitCost=.08m,CostReviewed=true};Check(derived.TotalCost==80m,"custo unitário calcula total");
        var cash=new CashSession{OpeningAmount=200,Sales=[new Sale{PaymentType="Dinheiro",TotalValue=850},new Sale{PaymentType="PIX",TotalValue=500}],Withdrawals=[new CashWithdrawal{Amount=100},new CashWithdrawal{Amount=200}]};Check(cash.ExpectedCash==750&&cash.WithdrawalsTotal==300,"saldo esperado usa apenas dinheiro e soma sangrias");
        var data=new AppData{Sessions=[new CashSession()],FinancialIntegrityVersion=1,OrderDetailsVersion=7};var paid=new ScheduledOrder{Number=1,OrderTotal=178,CustomerName="Cliente"};data.ScheduledOrders.Add(paid);OrderOperations.AddPayment(data,paid,178,"PIX",new DateTime(2026,9,20),"Pagamento",Guid.NewGuid(),Admin);Check(paid.RemainingAmount==0&&!paid.PayAfterDelivery&&paid.Payments.Single().PaidAt.Date==new DateTime(2026,9,20),"pagamento integral sem dívida e data real preservada");
        var partial=new ScheduledOrder{Number=2,OrderTotal=178,CustomerName="Cliente",PayAfterDelivery=true};data.ScheduledOrders.Add(partial);OrderOperations.AddPayment(data,partial,50,"PIX",DateTime.Today,"Parcela",Guid.NewGuid(),Admin);Check(partial.RemainingAmount==128&&partial.PayAfterDelivery,"recebimento parcial gera somente saldo restante");
        var root=Path.Combine(Path.GetTempPath(),"erp-prompt10-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var store=new DataStore(root);store.EnsureInitialized();var order=new ScheduledOrder{Number=10,CustomerName="Cliente",OrderTotal=100,ItemCostsEnabled=true,Items=[cost]};store.Update(d=>{d.ScheduledOrders.Add(order);return 0;});var loaded=store.Load().ScheduledOrders.Single();Check(loaded.Items.Single().EnteredTotalCost==95&&loaded.ItemsCost==95,"salvar e reabrir preserva custo total exato");}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
        Console.WriteLine($"TOTAL: {passed} testes do Prompt 10 aprovados");
    }
}
