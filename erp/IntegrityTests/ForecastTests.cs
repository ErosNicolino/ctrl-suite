using GraficaCaixa;

// Fluxo previsto (2026.10.23.2): despesas vencidas contam hoje, as próximas pelo vencimento, recorrentes futuras viram "previstas".
public static class ForecastTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var today=new DateTime(2026,10,23);var monday=new DateTime(2026,10,19);
        var data=new AppData();var state=data.Prompt12??=new();
        state.Expenses.Add(new Expense{Description="Luz",Amount=300,DueDate=today.AddDays(-5),Competence=new(2026,10,1),Status=ExpenseStatus.Pending});
        state.Expenses.Add(new Expense{Description="Internet",Amount=120,DueDate=today.AddDays(10),Competence=new(2026,10,1),Status=ExpenseStatus.Pending});
        state.Expenses.Add(new Expense{Description="Paga",Amount=999,DueDate=today.AddDays(2),Competence=new(2026,10,1),Status=ExpenseStatus.Paid});
        state.Expenses.Add(new Expense{Description="Aluguel",Amount=2000,DueDate=new(2026,10,10),Competence=new(2026,10,1),Status=ExpenseStatus.Paid,Recurring=true,RecurrenceMonths=1,RecurrenceKey="aluguel"});
        var f=CashForecast.Build(data,today);
        Check(f.Weeks.Count==5&&f.Weeks[0].Start==monday&&f.Weeks[4].End==monday.AddDays(34),"5 semanas a partir da segunda-feira");
        Check(f.Items.Any(x=>x.Title=="Luz"&&x.Overdue&&x.Date==today),"despesa vencida conta hoje");
        Check(f.Items.Any(x=>x.Title=="Internet"&&x.Date==today.AddDays(10))&&f.Weeks[2].Out==120,"despesa futura no vencimento (02/11, 3ª semana)");
        Check(!f.Items.Any(x=>x.Title=="Paga"),"despesa paga fica de fora");
        Check(f.Items.Any(x=>x.Title=="Aluguel"&&x.Projected&&x.Date==new DateTime(2026,11,10)),"recorrente de novembro prevista no dia 10");
        Check(f.TotalOut==300+120+2000&&f.TotalIn==0,"totais da previsão");
        Console.WriteLine($"FORECAST PASSED: {passed}");
    }
}
