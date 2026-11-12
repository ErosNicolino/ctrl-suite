using GraficaCaixa;

// Link "acompanhe seu pedido" (2026.10.23.2): assinatura do link, validade, e a página não expõe telefone, itens nem valores.
public static class TrackingTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run(string? output)
    {
        var key=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);var other=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);var id=Guid.NewGuid();
        var token=OrderTracking.Token(key,id);
        Check(token.Length==43&&OrderTracking.Read(key,token)==id,"link volta para o mesmo pedido");
        Check(OrderTracking.Read(other,token)==null,"link de outra chave é recusado");
        var tampered=(token[0]=='A'?'B':'A')+token[1..];Check(OrderTracking.Read(key,tampered)==null,"link alterado é recusado");
        var shortToken=OrderTracking.ShortToken(key,55,id);IEnumerable<Guid> Ids(long n)=>n==55?[Guid.NewGuid(),id]:[];
        Check(shortToken.StartsWith("55-")&&shortToken.Length==14&&OrderTracking.Read(key,shortToken,Ids)==id,"link curto (número-código) volta para o mesmo pedido");
        Check(OrderTracking.Read(key,token,Ids)==id,"link longo antigo continua valendo");
        var badShort=shortToken[..^1]+(shortToken[^1]=='A'?'B':'A');
        Check(OrderTracking.Read(key,badShort,Ids)==null&&OrderTracking.Read(other,shortToken,Ids)==null&&OrderTracking.Read(key,"56"+shortToken[2..],Ids)==null,"link curto alterado, de outra chave ou de outro número é recusado");
        Check(OrderTracking.Read(key,"-1-AAAAAAAAAAA",Ids)==null&&OrderTracking.Read(key,"55-",Ids)==null&&OrderTracking.Read(key,"1234567890-AAAAAAAAAAA",Ids)==null,"links curtos malformados são recusados");
        Check(OrderTracking.Read(key,"")==null&&OrderTracking.Read(key,"../../etc")==null&&OrderTracking.Read(key,new string('a',43))==null,"links inválidos são recusados");
        var order=new ScheduledOrder{Id=id,Number=55,CustomerName="Beto Souza",Contact="11940356267",Stage=OrderStages.Production,ProductionBusinessDays=3,DueDate=DateTime.Today.AddDays(2),OrderTotal=323m,UseItemPrices=true,Items=[new OrderItem{Quantity=2,Product="Lona secreta 1,90x0,92",UnitPrice=161.5m}]};
        var company=new CompanyProfile{Name="Gráfica <Teste>",Phone="11 98765-1002",Address="Rua A, 1"};
        var page=OrderTracking.Page(order,company,false);
        Check(page.Contains("#55 · Beto")&&!page.Contains("Souza"),"mostra só o primeiro nome");
        Check(!page.Contains("94035")&&!page.Contains("Lona secreta")&&!page.Contains("323")&&!page.Contains("161"),"sem telefone do cliente, itens nem valores");
        Check(page.Contains("&lt;Teste&gt;")&&!page.Contains("<Teste>"),"texto escapado (sem HTML injetado)");
        Check(!page.Contains("<script")&&!page.Contains("style=\""),"sem scripts nem estilos em atributo (CSP)");
        Check(OrderTracking.Page(order,company,true).Contains("Valor a pagar"),"saldo só quando ligado");
        var trackingTemplate=CommunicationOperations.DefaultTemplates().Single(x=>x.Name==CommunicationOperations.TrackingTemplateName).Body;
        var rendered=MessageTemplates.Render(trackingTemplate,new Dictionary<string,string>{["cliente"]="Katia",["pedido"]="#90",["link"]="https://pedido.x/p/90-abc",["grafica"]="Gráfica"});
        Check(rendered.StartsWith("Olá, Katia!")&&rendered.Contains("*#90*")&&rendered.Contains("\nhttps://pedido.x/p/90-abc\n")&&rendered.EndsWith("*Gráfica*"),"mensagem do link: link em linha própria e assinatura da gráfica");
        Check(OrderTracking.Page(order,company,false,"https://pedido.x").Contains("og:image\" content=\"https://pedido.x/p/logo.png\"")&&!page.Contains("og:image"),"logotipo na prévia do WhatsApp só com endereço completo");
        var msg=CommunicationOperations.TrackingMessage(null,company,"Katia",90,"https://pedido.x/p/90-abc");
        Check(msg.Contains("*#90*")&&msg.Contains("https://pedido.x/p/90-abc"),"mensagem do link para o celular usa o modelo");
        Check(CommunicationOperations.TrackingMessage(new Prompt12State{CommunicationTemplates=[new CommunicationTemplate{Name=CommunicationOperations.TrackingTemplateName,Body="Oi {cliente}",Active=true}]},company,"Katia",90,"https://u").EndsWith("\n\nhttps://u"),"modelo sem {link} ainda leva o link");
        Check(page.Contains("og:title")&&!System.Net.WebUtility.HtmlDecode(page).Contains("og:description\" content=\"Beto"),"prévia do WhatsApp com título, sem dados do cliente");
        var withPix=company with {PixKey="contato@grafica.com",City="São Paulo"};
        Check(OrderTracking.Page(order,withPix,true).Contains("<div class=\"pix\">000201")&&!OrderTracking.Page(order,withPix,false).Contains("class=\"pix\"")&&!OrderTracking.Page(order,company,true).Contains("class=\"pix\""),"Pix copia e cola só com saldo ligado e chave Pix");
        Check(OrderTracking.ReviewLink("https://g.page/r/abc/review")!=null&&OrderTracking.ReviewLink("javascript:alert(1)")==null&&OrderTracking.ReviewLink("http://x.com")==null,"link de avaliação só https");
        var reviewCompany=company with {ReviewUrl="https://g.page/r/abc/review"};
        Check(!OrderTracking.Page(order,reviewCompany,false).Contains("Avaliar no Google"),"avaliação não aparece antes da entrega");
        var hoursPage=System.Net.WebUtility.HtmlDecode(OrderTracking.Page(order,company,false));
        Check(hoursPage.Contains("Segunda a sexta: 9h às 18h<br>Sábado: 9h às 13h"),"horário de funcionamento embaixo do endereço");
        order.Stage=OrderStages.Ready;var readyPage=System.Net.WebUtility.HtmlDecode(OrderTracking.Page(order,company,false));order.Stage=OrderStages.Production;
        Check(readyPage.Contains("<b>Horário de funcionamento</b><div>Segunda a sexta: 9h às 18h</div><div>Sábado: 9h às 13h</div>"),"pedido pronto mostra o horário no cartão de retirada");
        Check(!OrderTracking.Page(order,company with {OpeningHours=""},false).Contains("Hor&#225;rio")&&!OrderTracking.Page(order,company with {OpeningHours=""},false).Contains("Horário")&&!OrderTracking.Page(order,company with {OpeningHours="<b>x"},false).Contains("<b>x"),"sem horário não mostra nada; texto escapado");
        Check(page.Contains("<img src=\""+OrderTracking.LogoFile+"\"")&&OrderTracking.NotFoundPage(company).Contains("<img src=\""+OrderTracking.LogoFile+"\""),"logotipo da gráfica no topo (também no link expirado)");
        Check(OrderTracking.Visible(order,DateTime.Now,7),"pedido em produção aparece");
        order.Stage=OrderStages.Delivered;Check(OrderTracking.Page(order,reviewCompany,false).Contains("href=\"https://g.page/r/abc/review\""),"avaliação aparece depois de entregue");
        order.ReadyAt=DateTime.Today.AddDays(-8);Check(!OrderTracking.Visible(order,DateTime.Now,7),"some 7 dias depois de pronto/entregue");
        order.Stage=OrderStages.Production;order.CancelledAt=DateTime.Now;Check(!OrderTracking.Visible(order,DateTime.Now,7),"pedido cancelado não aparece");
        order.CancelledAt=null;order.Stage=OrderStages.Ready;order.ReadyAt=DateTime.Today;
        if(output!=null){File.WriteAllText(output,OrderTracking.Page(order,withPix,true));order.Stage=OrderStages.Ready;File.WriteAllText(Path.ChangeExtension(output,null)+"-pronto.html",OrderTracking.Page(order,withPix,true));order.Stage=OrderStages.Delivered;File.WriteAllText(Path.ChangeExtension(output,null)+"-entregue.html",OrderTracking.Page(order,reviewCompany,false));}
        Console.WriteLine($"TRACKING PASSED: {passed}");
    }
}
