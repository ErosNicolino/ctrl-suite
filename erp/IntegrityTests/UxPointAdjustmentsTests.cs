using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GraficaCaixa;
using GraficaCaixa.Wpf;

static class UxPointAdjustmentsTests
{
    static int passed;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FALHOU: " + name);
        Console.WriteLine("OK  " + name);
        passed++;
    }

    public static void Run()
    {
        var root = Path.GetFullPath(Path.Combine("work", "ux-point-adjustments-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(root);
        var store = new DataStore(Path.Combine(root, "db"));
        store.EnsureInitialized();
        var pin = "246810";
        var credential = OwnerSecurity.Create(pin);
        var orderId = Guid.NewGuid();
        store.Update(d =>
        {
            d.Prompt12.OwnerPin = credential;
            d.Customers.Add(new CustomerProfile { Name = "Cliente Fiscal", Contact = "11999999999", CpfCnpj = "12.345.678/0001-90", Email = "fiscal@cliente.test" });
            d.ScheduledOrders.Add(new ScheduledOrder
            {
                Id = orderId,
                Number = 81,
                CustomerName = "Cliente Fiscal",
                Contact = "11999999999",
                Stage = "Produção",
                OrderTotal = 215,
                OriginalTotal = 215,
                UseItemPrices = true,
                Items = [new OrderItem { Quantity = 250, Product = "Cartão de visita 300g", UnitPrice = .86m }]
            });
            d.Prompt12.Expenses.Add(new Expense
            {
                Description = "Energia",
                Category = "Operacional",
                Amount = 320,
                Competence = new DateTime(2026, 9, 1),
                DueDate = new DateTime(2026, 9, 25),
                PaymentMethod = "PIX",
                Notes = "Conta mensal",
                Recurring = true,
                RecurrenceKey = "energia"
            });
            return 0;
        });
        var expectedDescription = "250 - Cartão de visita 300g - R$ 215,00";
        var loadedOrder = store.Load().ScheduledOrders.Single(x => x.Id == orderId);
        var description = OrderFiscalWindow.BuildFiscalDescription(loadedOrder);
        Check(description == expectedDescription, "descrição da NF usa somente quantidade, descrição e subtotal real");
        Check(!description.Contains("0,86") && !description.Contains("11999999999") && !description.Contains("PIX"), "descrição da NF não expõe unitário, telefone ou pagamento");
        Check(OrderFiscalWindow.NationalIssuerUrl == "https://www.nfse.gov.br/EmissorNacional/Login?ReturnUrl=%2FEmissorNacional", "URL do emissor nacional é exatamente a solicitada");
        Check(OrderFiscalWindow.CopyableDocument("123.456.789-09") == "12345678909" && OrderFiscalWindow.CopyableDocument("12.abc.345/01de-35") == "12ABC34501DE35" && OrderFiscalWindow.CopyableDocument(null) == "", "CPF/CNPJ é copiado sem pontuação e mantém letras do CNPJ novo");
        Check(OrderFiscalWindow.CopyableValue(1234.5m) == "1234,50", "valor é copiado sem R$ e sem ponto de milhar");

        Console.WriteLine($"TOTAL: {passed} verificações aprovadas. Imagens: {root}");
    }
}
