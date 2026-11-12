using GraficaCaixa;

// Curva ABC dos Relatórios: mesma base do faturamento (pedidos, balcão com itens e vendas diretas), classes 80/95 e margem só com custo completo.
public static class AbcCurveTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var day=new DateTime(2026,10,10,10,0,0);var start=new DateTime(2026,10,1);var end=new DateTime(2026,11,1);
        var caneca=new Product{Description="Caneca Porcelana",Price=40};var cliente=Guid.NewGuid();
        var orders=new List<ScheduledOrder>
        {
            new(){Number=1,CustomerId=cliente,CustomerName="Maria",CreatedAt=day,OrderTotal=800,Items=[new OrderItem{ProductId=caneca.Id,Product="Caneca",Quantity=20,UnitPrice=40,UnitCost=12}]},
            new(){Number=2,CustomerId=cliente,CustomerName="Maria",CreatedAt=day,OrderTotal=100,Items=[new OrderItem{Product="Banner 1x1",Quantity=1,UnitPrice=100}]},
            new(){Number=3,CustomerName="João",CreatedAt=day,OrderTotal=50,Items=[new OrderItem{Product="Cartão de visita",Quantity=100,UnitPrice=0.5m,UnitCost=0.1m}]},
            new(){Number=4,CustomerName="Cancelado",CreatedAt=day,OrderTotal=999,CancelledAt=day,Items=[new OrderItem{Product="Não conta",Quantity=1,UnitPrice=999}]},
            new(){Number=5,CustomerName="Fora",CreatedAt=day.AddMonths(-2),OrderTotal=999,Items=[new OrderItem{Product="Fora do período",Quantity=1,UnitPrice=999}]},
        };
        var sinal=new Sale{Product="Sinal do pedido #1",TotalValue=400,PaymentType="PIX",CreatedAt=day};
        var direta=new Sale{Product="Xerox",TotalValue=30,Units=60,PaymentType="Dinheiro",CreatedAt=day};
        var session=new CashSession{OpenedAt=day.Date,ClosedAt=day.Date.AddHours(20),Sales=[sinal,direta]};
        var balcao=new ManualDebt{CustomerName="Ana",CreatedAt=day,Amount=20,Items=[new OrderItem{Product="caneca  porcelana",Quantity=1,UnitPrice=20}]};
        var data=new AppData{Products=[caneca],ScheduledOrders=orders,Sessions=[session],ManualDebts=[balcao]};
        var (products,customers)=AbcCurve.Of(data,start,end);

        Check(products.Sum(x=>x.Revenue)==1000,"base: 800+100+50 (pedidos) + 30 (venda direta) + 20 (balcão); cancelado, fora do período e sinal ficam de fora");
        Check(products.Count==4&&products[0].Name=="Caneca Porcelana"&&products[0].Revenue==820&&products[0].Quantity==21,"caneca do pedido e do balcão somam numa linha só (pelo cadastro e pelo nome)");
        Check(products[0].Class=="A"&&products[1].Name=="Banner 1x1"&&products[1].Class=="B","82% é A; o próximo (acumulado antes 82%) é B");
        Check(products.Single(x=>x.Name=="Xerox").Class=="C"&&products.Last().Cumulative==100,"o fim da lista é C e o acumulado fecha em 100%");
        Check(products[0].Margin==null,"caneca sem custo no balcão: margem não apurada (custo pendente não é zero)");
        Check(products.Single(x=>x.Name=="Cartão de visita").Margin==80,"cartão de visita: custo 10 sobre 50 = margem 80%");
        Check(customers[0].Name=="Maria"&&customers[0].Revenue==900&&customers[0].Count==2&&customers[0].Class=="A","Maria: 2 pedidos, R$ 900, classe A");
        Check(customers.Any(x=>x.Name=="Balcão (sem nome)"&&x.Revenue==30),"venda direta sem cliente aparece como Balcão (sem nome)");
        var empty=AbcCurve.Of(new AppData(),start,end);
        Check(empty.Products.Count==0&&empty.Customers.Count==0,"período sem vendas: listas vazias");
        Console.WriteLine($"TOTAL: {passed} verificações da Curva ABC.");
    }
}
