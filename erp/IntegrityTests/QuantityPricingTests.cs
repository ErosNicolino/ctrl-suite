using GraficaCaixa;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GraficaCaixa.Wpf;

static class QuantityPricingTests
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("OK "+name);}
    static Product Make(string name,decimal basePrice,decimal[] prices)
    {
        int[] from=[1,6,16,31,61,101,301,501];int?[] through=[5,15,30,60,100,300,500,null];
        return new Product{Description=name,Price=basePrice,UseQuantityPricing=true,QuantityPriceTiers=Enumerable.Range(0,8).Select(i=>new QuantityPriceTier{From=from[i],Through=through[i],UnitPrice=prices[i]}).ToList()};
    }
    public static void Run()
    {
        var bw=Make("Impressão A4 P/B - Offset 75g",1m,[1m,.9m,.8m,.75m,.7m,.65m,.6m,.55m]);
        var color=Make("Impressão A4 Colorida - Offset 75g",2m,[2m,1.8m,1.7m,1.6m,1.5m,1.4m,1.3m,1m]);
        QuantityPricing.Validate(bw);QuantityPricing.Validate(color);
        int[] quantities=[1,5,6,15,16,30,31,60,61,100,101,300,301,500,501,1000];
        foreach(var product in new[]{bw,color})foreach(var quantity in quantities)
        {
            var tier=QuantityPricing.Find(product,quantity)??throw new Exception("Sem faixa");
            Check(tier.From<=quantity&&(tier.Through==null||quantity<=tier.Through)&&QuantityPricing.UnitPrice(product,quantity)==tier.UnitPrice,$"{product.Description} / {quantity}");
        }
        Check(QuantityPricing.UnitPrice(bw,50)==.75m&&50*QuantityPricing.UnitPrice(bw,50)==37.5m,"P/B 50");
        Check(QuantityPricing.UnitPrice(color,20)==1.7m&&20*QuantityPricing.UnitPrice(color,20)==34m,"Colorida 20");
        Check(QuantityPricing.UnitPrice(bw,70)==.7m&&70*QuantityPricing.UnitPrice(bw,70)==49m,"P/B 70");
        var normal=new Product{Description="Produto comum",Price=12.34m};
        Check(QuantityPricing.Find(normal,500)==null&&QuantityPricing.UnitPrice(normal,500)==12.34m,"Produto normal");
        var quantityBox=new TextBox{Text="50"};var priceBox=new TextBox();var hint=new TextBlock();var stack=new StackPanel();stack.Children.Add(quantityBox);stack.Children.Add(priceBox);stack.Children.Add(hint);
        var binding=new QuantityTierBinding(quantityBox,priceBox,hint,()=>false);binding.Choose(bw);
        Check(priceBox.Text=="0,75"&&hint.Visibility==Visibility.Visible,"Pedido e lançamento recebem preço da faixa");
        quantityBox.Text="70";Check(priceBox.Text=="0,70","Mudança de quantidade troca faixa");
        var window=new Window{Content=stack,Width=300,Height=200};window.Show();priceBox.Focus();Keyboard.Focus(priceBox);priceBox.Text="0,66";quantityBox.Text="100";
        Check(priceBox.Text=="0,66","Valor unitário manual preservado");window.Close();
        var tempStore=new DataStore(Path.Combine(Environment.CurrentDirectory,"work","prompt8","tmp","tiers-"+Guid.NewGuid().ToString("N")));tempStore.EnsureInitialized();
        tempStore.Update(d=>{ProductOperations.Save(d,bw,null,OperationContext.Administrator("Teste"));return 0;});
        var loaded=tempStore.Load().Products.Single(x=>x.Id==bw.Id);
        Check(loaded.UseQuantityPricing&&loaded.QuantityPriceTiers.Count==8&&QuantityPricing.UnitPrice(loaded,501)==.55m,"Faixas persistem no cadastro");
        var gap=Make("Inválido",1m,[1m,.9m,.8m,.75m,.7m,.65m,.6m,.55m]);gap.QuantityPriceTiers[1].From=7;
        try{QuantityPricing.Validate(gap);throw new Exception("Lacuna aceita");}catch(InvalidOperationException){Check(true,"Lacuna rejeitada");}
        var groupId=Guid.NewGuid();var original=new Sale{PaymentGroupId=groupId,ItemsTotal=100,Units=1,CustomerName="Não informado",PaymentType="PIX",TotalValue=100,Items=[new OrderItem{Quantity=1,Product="Teste",UnitPrice=100}]};
        var cash=new CashSession{Sales=[original]};var debt=new ManualDebt{Id=groupId,Amount=100,SaleIds=[original.Id],Items=JsonSerializer.Deserialize<List<OrderItem>>(JsonSerializer.Serialize(original.Items))!};
        var data=new AppData{Sessions=[cash],ManualDebts=[debt]};var admin=OperationContext.Administrator("Teste");
        var change=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(original))!;change.Items[0].Quantity=2;change.Units=2;change.ItemsTotal=200;
        FinancialOperations.EditSale(data,original,change,"Quantidade corrigida",admin);
        Check(debt.Amount==200&&original.Items[0].Quantity==2&&original.Total==100&&debt.Remaining(data)==100,"Quantidade corrige saldo sem alterar recebido");
        var smaller=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(original))!;smaller.Items[0].Quantity=1;smaller.Items[0].UnitPrice=80;smaller.Units=1;smaller.ItemsTotal=80;
        try{FinancialOperations.EditSale(data,original,smaller,"Quantidade corrigida",admin);throw new Exception("Recebido maior que total aceito");}catch(InvalidOperationException){Check(true,"Recebido acima do total rejeitado");}
        var oldSale=new Sale{CustomerName="Não informado",Contact="",Product="Descrição inicial",PaymentType="PIX",TotalValue=25};
        var oldSession=new CashSession{OpenedAt=DateTime.Now.AddDays(-20),ClosedAt=DateTime.Now.AddDays(-19),Sales=[oldSale]};
        var history=new AppData{Sessions=[oldSession]};for(var i=0;i<5;i++)history.Sessions.Add(new CashSession{OpenedAt=DateTime.Now.AddDays(-i-1),ClosedAt=DateTime.Now.AddDays(-i)});
        var simple=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(oldSale))!;simple.CustomerName="Cliente corrigido";simple.Contact="11999999999";simple.Product="Descrição corrigida";
        FinancialOperations.EditSale(history,oldSale,simple,"",admin);
        Check(oldSale.CustomerName=="Cliente corrigido"&&oldSale.Contact.Length>0&&history.CashClosureVersions.Count==0,"Dados cadastrais de caixa antigo editáveis com auditoria");
        var scaleGroup=Guid.NewGuid();var scaleSale=new Sale{PaymentGroupId=scaleGroup,CustomerName="Não informado",PaymentType="PIX",Units=2,ItemsTotal=10m,TotalValue=10m,Items=[new OrderItem{Quantity=2,Product="Impressão",UnitPrice=5m}]};
        var scaleData=new AppData{Sessions=[new CashSession{Sales=[scaleSale]}],ManualDebts=[new ManualDebt{Id=scaleGroup,Amount=10m,SaleIds=[scaleSale.Id],Items=JsonSerializer.Deserialize<List<OrderItem>>(JsonSerializer.Serialize(scaleSale.Items))!}]};
        var scaleDraft=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(scaleSale))!;scaleDraft.CustomerName="Cliente incluído";scaleDraft.Items[0].UnitPrice=5.00m;scaleDraft.ItemsTotal=10.00m;
        FinancialOperations.EditSale(scaleData,scaleSale,scaleDraft,"",admin);
        Check(scaleSale.CustomerName=="Cliente incluído"&&scaleData.FinancialCorrections.Count==0,"Preço numericamente igual não bloqueia inclusão do nome");
        var receiptOrder=new ScheduledOrder{Number=123,CustomerName="Cliente teste",Contact="11999999999",OrderTotal=100,Description="Teste",Items=[new OrderItem{Quantity=2,Product="Impressão",UnitPrice=50}]};
        var receiptPath=Path.Combine(Environment.CurrentDirectory,"work","prompt8","tmp","recibo-teste.pdf");
        ReportWriter.WriteReceipt(receiptOrder,receiptPath);
        var pdf=System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(receiptPath));
        Check(pdf.Contains("/MediaBox [0 0 595.28 841.89]")&&pdf.Contains("q 1 0 0 1 0 422.36 cm"),"Recibo A5 no topo de A4");
    }
}
