using GraficaCaixa;
using GraficaCaixa.Wpf;

// Pesquisa da tela de Lembretes.
public static class ReminderSearchTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var quick=new Reminder{Phone="11910097914",Name="Bazar Kamy",Description="Aguardando aprovação orçamento calendário"};
        var titled=new Reminder{Title="300 - Cartões de Visita",Phone="",Name="",Description=""};
        Check(RemindersWindow.Matches(quick,quick.Name,""),"pesquisa vazia mostra tudo");
        Check(RemindersWindow.Matches(quick,quick.Name,"  kamy "),"acha pelo nome, sem diferenciar maiúsculas");
        Check(RemindersWindow.Matches(quick,quick.Name,"aprovacao orcamento"),"acha pelo texto, sem acentos");
        Check(RemindersWindow.Matches(titled,titled.Name,"cartoes"),"acha pelo título");
        Check(RemindersWindow.Matches(quick,"","91009"),"acha por parte do telefone");
        Check(RemindersWindow.Matches(quick,"","(11) 91009-7914"),"acha pelo telefone formatado");
        Check(RemindersWindow.Matches(new Reminder{Phone="11910097914"},"Cliente Cadastrado","cadastrado"),"acha pelo nome do cliente vindo do telefone");
        Check(!RemindersWindow.Matches(quick,quick.Name,"banner"),"texto diferente não acha");
        Check(!RemindersWindow.Matches(quick,quick.Name,"banner 11"),"número junto de palavra não procura só no telefone");
        Check(!RemindersWindow.Matches(titled,titled.Name,"91009"),"lembrete sem telefone não acha por número");
        Console.WriteLine($"LEMBRETES: {passed} testes aprovados");
    }
}
