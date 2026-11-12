using GraficaCaixa;
using GraficaCaixa.Wpf;

// Datas comemorativas (Regras): regras fixas e móveis, próxima data dentro do aviso, mesma época do ano passado e sugestões.
public static class SeasonalTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    public static void Run()
    {
        Check(SeasonalCalendar.DateOf("2º domingo de maio",2026)==new DateTime(2026,5,10)&&SeasonalCalendar.DateOf("2º domingo de maio",2025)==new DateTime(2025,5,11),"Dia das Mães: 10/05/2026 e 11/05/2025");
        Check(SeasonalCalendar.DateOf("2o domingo de agosto",2026)==new DateTime(2026,8,9),"Dia dos Pais aceita 2o (sem º)");
        Check(SeasonalCalendar.DateOf("Páscoa",2027)==new DateTime(2027,3,28)&&SeasonalCalendar.DateOf("12/06",2026)==new DateTime(2026,6,12),"Páscoa 2027 e data fixa");
        Check(SeasonalCalendar.DateOf("31/02",2026)==null&&SeasonalCalendar.DateOf("5º domingo de fevereiro",2026)==null&&!SeasonalCalendar.TryParse("Sem regra",out _,out _),"regras impossíveis recusadas");
        var rules=new BusinessRules();
        Check(rules.UpcomingSeason(new DateTime(2026,10,1)) is {Name:"Dia das Crianças"} s1&&s1.Date==new DateTime(2026,10,12),"01/10: próxima é Dia das Crianças (12/10)");
        Check(rules.UpcomingSeason(new DateTime(2026,12,20))?.Name=="Natal"&&rules.UpcomingSeason(new DateTime(2026,3,10))?.Name=="Páscoa"&&rules.UpcomingSeason(new DateTime(2026,7,1))==null,"Natal, Páscoa (05/04/2026) e julho sem data no aviso de 30 dias");
        Check(rules.UpcomingSeason(new DateTime(2026,12,28))==null&&rules.UpcomingSeason(new DateTime(2027,1,10))?.Date==new DateTime(2027,2,1),"virada do ano: volta às aulas de 2027");
        var (from,to)=SeasonalSuggestions.LastYearWindow(rules,new DateTime(2026,5,10),"2º domingo de maio");
        Check(from==new DateTime(2025,4,11)&&to==new DateTime(2025,5,12),"mesma época do ano passado: 30 dias antes de 11/05/2025, inclusive");
        var bad=new BusinessRules{SeasonalDates=["Natal: 25/12","Festa: 40/13"]};Check(Throws(bad.Validate),"Regras recusam data inválida");
        Check(Throws(new BusinessRules{SeasonalLeadDays=0}.Validate)&&!Throws(new BusinessRules().Validate),"aviso entre 1 e 90 dias; padrão válido");
        var data=new AppData{ScheduledOrders=[
            new(){Number=1,CustomerName="Maria Souza",Contact="11 98888-7777",CreatedAt=new DateTime(2025,5,2),OrderTotal=300,Items=[new OrderItem{Product="Caneca",Quantity=10,UnitPrice=30}]},
            new(){Number=2,CustomerName="maria souza",Contact="",CreatedAt=new DateTime(2025,5,5),OrderTotal=60,Items=[new OrderItem{Product="Caneca",Quantity=2,UnitPrice=30}]},
            new(){Number=4,CustomerName="Não informado",Contact="11 96666-5555",CreatedAt=new DateTime(2025,5,3),OrderTotal=50,Items=[new OrderItem{Product="Caneca",Quantity=1,UnitPrice=50}]},
            new(){Number=3,CustomerName="Fora",Contact="11 97777-6666",CreatedAt=new DateTime(2025,6,1),OrderTotal=999,Items=[new OrderItem{Product="Outro",Quantity=1,UnitPrice=999}]}]};
        var insight=SeasonalSuggestions.Of(data,"Dia das Mães",new DateTime(2026,5,10),from,to);
        Check(insight.Products.Single().Name=="Caneca"&&insight.Products.Single().Quantity==13,"mais vendidos só da época (13 canecas)");
        Check(insight.Customers.Single() is {Name:"Maria Souza",Total:360,Count:2}&&insight.Customers.Single().Phone=="11 98888-7777","cliente agrupado pelo nome, com o WhatsApp de algum pedido; \"Não informado\" fica de fora");
        var message=SeasonalSuggestions.Message(insight,insight.Customers[0],new CompanyProfile{Name="Gráfica X"});
        Check(message.StartsWith("Olá, Maria!")&&message.Contains("Dia das Mães")&&message.Contains("10/05")&&message.Contains("Caneca"),"mensagem com nome, data e sugestão");
        Console.WriteLine($"TOTAL: {passed} verificações das datas comemorativas.");
    }
}
