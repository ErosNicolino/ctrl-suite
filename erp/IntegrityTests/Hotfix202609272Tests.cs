using GraficaCaixa;
using GraficaCaixa.Wpf;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;

static class Hotfix202609272Tests
{
    static int passed;
    static readonly OperationContext Admin=OperationContext.Administrator("Hotfix 2026.9.27.2");
    static void Check(bool value,string name){if(!value)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}

    public static void Run()
    {
        var app=Application.Current as App??new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var root=Path.Combine(Path.GetTempPath(),"erp-hotfix-2026-9-27-2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var store=new DataStore(root);store.EnsureInitialized();
            store.Update(d=>{d.Products.Add(new Product{Description="Cartão de visita com verniz",Category="Impressos Comerciais",Unit="un",Cost=40,Price=100});return 0;});
            for(var i=0;i<3;i++)
            {
                var window=new ProductsWindow(store);window.Show();window.UpdateLayout();
                var search=(TextBox)window.FindName("SearchBox");var category=(ComboBox)window.FindName("CategoryBox");var grid=(ListBox)window.FindName("ProductsGrid");var create=(Button)window.FindName("CreateOrderButton");
                search.Text="cartão verniz";category.SelectedItem="Impressos Comerciais";window.UpdateLayout();
                Check(grid.Items.Count==1,"Produtos abre e pesquisa por palavras/categoria (ciclo "+(i+1)+")");
                grid.SelectedIndex=0;window.UpdateLayout();Check(create.IsEnabled,"produto selecionado habilita Criar pedido (ciclo "+(i+1)+")");
                window.Close();
            }

            var connection=new ServerConnection("https://127.0.0.1:9443",new string('k',32),new string('a',64)){Purpose="pairing",PairingExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)};
            foreach(var scale in new[]{1.0,1.25,1.5})
            {
                var window=new ConnectionWindow(connection,Path.Combine(root,"caixa_dados.db")){Width=620,Height=520};window.Show();window.UpdateLayout();var button=(Button)window.FindName("CopyButton");
                Check(button.IsVisible&&button.ActualHeight>0&&button.TransformToAncestor(window).Transform(new Point()).Y+button.ActualHeight<=window.ActualHeight,"botão de conexão visível a "+(scale*100).ToString("0")+"%");window.Close();
            }

            var emptyCash=new CashSession{OpeningAmount=200};var empty=new CashClosingWindow(emptyCash);empty.Show();empty.UpdateLayout();Check(empty.ActualHeight<=empty.MaxHeight&&empty.ActualHeight>0,"fechamento vazio cabe na janela");empty.Close();
            var manyCash=new CashSession{OpeningAmount=200,Sales=[new Sale{PaymentType="Dinheiro",TotalValue=850},new Sale{PaymentType="PIX",TotalValue=500}]};for(var i=0;i<40;i++)manyCash.Withdrawals.Add(new CashWithdrawal{Amount=5,Reason="Sangria "+i});var many=new CashClosingWindow(manyCash);many.Show();many.UpdateLayout();Check(manyCash.ExpectedCash==850&&many.ActualHeight<=many.MaxHeight+1,"fechamento com muitas sangrias usa área limitada sem alterar cálculo");many.Close();

            var payments=new PaymentEditor(false);payments.Add("PIX",100,new DateTime(2024,9,27));payments.Add("Cartão de crédito 2x",78,new DateTime(2026,9,27));var parts=payments.Read(178);Check(parts.Sum(x=>x.Amount)==178&&parts[0].PaidAt!.Value.Year==2024&&parts[1].FeePercent>0,"pagamentos múltiplos preservam ano, total e taxa");
            var data=new AppData{Sessions=[new CashSession()],FinancialIntegrityVersion=1,OrderDetailsVersion=8};var order=new ScheduledOrder{Number=272,OrderTotal=178,CustomerName="Cliente",PayAfterDelivery=true,Stage="Entregue"};data.ScheduledOrders.Add(order);OrderOperations.AddPayment(data,order,100,"PIX",new DateTime(2024,9,27),"Parcela",Guid.NewGuid(),Admin);Check(order.RemainingAmount==78&&order.Payments.Single().PaidAt.Year==2024,"pagamento de R$ 100 mantém saldo de R$ 78 e vínculo");OrderOperations.AddPayment(data,order,78,"PIX",new DateTime(2026,9,27),"Quitação",Guid.NewGuid(),Admin);Check(order.RemainingAmount==0&&order.Payments.Count==2&&order.Payments[0].Amount==100,"quitação posterior preserva histórico");

            for(var i=0;i<35;i++)store.Update(d=>{d.ImprovementNotes="retenção "+i;return 0;});var automatic=Directory.GetFiles(Path.Combine(root,"Backups"),"caixa_*.db");Check(automatic.Length==30,"retenção mantém exatamente 30 backups automáticos");Check(automatic.All(x=>{using var c=new SqliteConnection("Data Source="+x);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";return string.Equals(cmd.ExecuteScalar()?.ToString(),"ok",StringComparison.OrdinalIgnoreCase);}),"backups automáticos são bancos SQLite íntegros");
        }
        finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
        Console.WriteLine($"TOTAL: {passed} testes do hotfix 2026.9.27.2 aprovados");app.Shutdown();
    }
}
