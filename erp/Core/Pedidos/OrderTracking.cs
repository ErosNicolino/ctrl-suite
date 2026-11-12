using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GraficaCaixa;

// "Acompanhe seu pedido" (desligado por padrão; liga em servidor.json › WebApi › Browser › Acompanhamento).
// O link leva o Id do pedido assinado com uma chave que só o servidor conhece (HMAC-SHA256): não dá para adivinhar nem trocar o pedido.
// A página é só leitura e sem scripts: primeiro nome, número, etapa, previsão e quantidade de itens. Não mostra telefone, itens nem valores
// (o saldo só com MostrarSaldo). Some 7 dias (DiasDepoisDeEntregue) depois que o pedido fica pronto/entregue, e nunca aparece para cancelados/arquivados.
public sealed record TrackingOptions
{
    public bool Ativo { get; init; }
    // Endereço público (ex.: https://pedido.suagrafica.com.br). Vazio = usa o endereço do celular (só funciona na rede da gráfica).
    public string Endereco { get; init; } = "";
    public bool MostrarSaldo { get; init; }
    public int DiasDepoisDeEntregue { get; init; } = 7;
}

public static class OrderTracking
{
    public static byte[] LoadOrCreateKey(string identityFolder)
    {
        var path = Path.Combine(identityFolder, "acompanhamento.key");
        if (File.Exists(path)) { var existing = File.ReadAllBytes(path); if (existing.Length == 32) return existing; }
        var key = RandomNumberGenerator.GetBytes(32); Directory.CreateDirectory(identityFolder); File.WriteAllBytes(path, key); return key;
    }

    public static string Token(byte[] key, Guid orderId)
    {
        var id = orderId.ToByteArray(); var mac = HMACSHA256.HashData(key, id); var buffer = new byte[32];
        id.CopyTo(buffer, 0); Array.Copy(mac, 0, buffer, 16, 16);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static Guid? Read(byte[] key, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 43 || token.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))) return null;
        byte[] buffer; try { buffer = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "="); } catch (FormatException) { return null; }
        if (buffer.Length != 32) return null;
        var id = buffer[..16]; var expected = HMACSHA256.HashData(key, id)[..16];
        return CryptographicOperations.FixedTimeEquals(expected, buffer[16..]) ? new Guid(id) : null;
    }

    // Link curto (WhatsApp): número do pedido + 11 caracteres de assinatura (64 bits, só o servidor gera; o limite de 120/min
    // torna inviável tentar adivinhar). Assinatura separada da do link longo; os links longos já enviados continuam valendo.
    public static string ShortToken(byte[] key, long number, Guid orderId) => number + "-" + ShortMac(key, orderId);
    static string ShortMac(byte[] key, Guid orderId) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, (byte[])[.. "curto:"u8, .. orderId.ToByteArray()])[..8]).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Aceita o link curto (número-assinatura) e o longo. idsByNumber devolve os pedidos com aquele número.
    public static Guid? Read(byte[] key, string token, Func<long, IEnumerable<Guid>> idsByNumber)
    {
        var dash = (token ?? "").IndexOf('-');
        if (dash is > 0 and <= 9 && token!.Length == dash + 12 && long.TryParse(token[..dash], System.Globalization.NumberStyles.None, null, out var number) && number > 0)
        {
            var mac = Encoding.ASCII.GetBytes(token[(dash + 1)..]);
            foreach (var id in idsByNumber(number))
                if (CryptographicOperations.FixedTimeEquals(mac, Encoding.ASCII.GetBytes(ShortMac(key, id)))) return id;
            return null;
        }
        return Read(key, token ?? "");
    }

    public static bool Visible(ScheduledOrder order, DateTime now, int daysAfter)
    {
        if (order.IsCancelled || order.IsUncollected) return false;
        if (order.Stage is not (OrderStages.Ready or OrderStages.Delivered)) return true;
        var since = order.ReadyAt ?? order.DueDate;
        return now.Date <= since.Date.AddDays(Math.Max(1, daysAfter));
    }

    static readonly string[] Steps = [OrderStages.Art, OrderStages.Approval, OrderStages.Production, OrderStages.Ready, OrderStages.Delivered];
    static string FirstName(string name) { var clean = (name ?? "").Trim(); var space = clean.IndexOf(' '); return space > 0 ? clean[..space] : clean.Length > 0 ? clean : "Cliente"; }

    // Cara da gráfica: logotipo no topo, faixa CMYK da marca, etapas em ciano (escurecido para leitura) e etapa atual em magenta.
    public const string LogoFile = "logo.svg", PreviewImageFile = "logo.png";
    public const string Style = "*{box-sizing:border-box}body{margin:0;background:#f7f9f8;color:#202a30;font:16px/1.45 system-ui,-apple-system,'Segoe UI',sans-serif}header{background:#fff;padding:14px 18px 12px;font-weight:700;text-align:center}header img{height:58px;width:auto;max-width:100%;display:block;margin:0 auto}.cmyk{display:flex;height:5px}.cmyk span{flex:1}.c{background:#00afef}.m{background:#ec268f}.y{background:#fff212}.k{background:#202a30}main{max-width:460px;margin:0 auto;padding:16px;display:flex;flex-direction:column;gap:12px}.card{background:#fff;border:1px solid #dce5e2;border-radius:14px;padding:16px}.muted{color:#5b6970;font-size:14px}h1{font-size:24px;margin:2px 0}ol{list-style:none;margin:8px 0 0;padding:0}li{display:flex;gap:12px;align-items:center;padding:7px 0;color:#5b6970}li i{width:22px;height:22px;border-radius:50%;border:2px solid #dce5e2;background:#fff;display:inline-flex;align-items:center;justify-content:center;font-style:normal;font-size:12px;color:#fff;flex:none}li.done{color:#202a30}li.done i{background:#0079a8;border-color:#0079a8}li.now{color:#202a30;font-weight:700}li.now i{border-color:#ec268f;box-shadow:0 0 0 4px #fce4f0}a.btn{display:block;text-align:center;background:#1e8e4e;color:#fff;text-decoration:none;font-weight:700;border-radius:12px;padding:13px}.big{font-size:22px;font-weight:700;color:#b54436}.center{text-align:center}.foot{text-align:center;color:#5b6970;font-size:13px}.hours{margin-top:10px;padding-top:10px;border-top:1px solid #dce5e2;font-size:14px;color:#5b6970}.hours b{display:block;color:#202a30;margin-bottom:2px}.pixlabel{margin-top:10px}.pix{font:13px/1.4 ui-monospace,Consolas,monospace;word-break:break-all;user-select:all;-webkit-user-select:all;background:#f2f6f5;border:1px dashed #9fb3ad;border-radius:10px;padding:10px;margin-top:6px}a.star{background:#fff;color:#202a30;border:1px solid #dce5e2;margin-top:10px}";
    // Link de avaliação: só https (evita "javascript:" e afins numa página pública).
    public static string? ReviewLink(string? url) =>
        Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    // Horário de funcionamento (Configurações › Empresa), uma linha por período.
    static string[] HoursLines(CompanyProfile company) => (company.OpeningHours ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static string HoursBlock(CompanyProfile company) => HoursLines(company) is { Length: > 0 } lines
        ? "<div class=\"hours\"><b>Horário de funcionamento</b>" + string.Concat(lines.Select(x => "<div>" + WebUtility.HtmlEncode(x) + "</div>")) + "</div>" : "";
    static string Header(CompanyProfile company) =>
        "<header><img src=\"" + LogoFile + "\" alt=\"" + WebUtility.HtmlEncode(company.Name) + "\"></header><div class=\"cmyk\"><span class=\"c\"></span><span class=\"m\"></span><span class=\"y\"></span><span class=\"k\"></span></div>";

    public static string Page(ScheduledOrder order, CompanyProfile company, bool showBalance, string? publicRoot = null)
    {
        string E(string? text) => WebUtility.HtmlEncode(text ?? "");
        var current = Array.IndexOf(Steps, order.Stage); if (current < 0) current = 0;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\">");
        // Prévia do link no WhatsApp: título e descrição sem dados do cliente.
        sb.Append("<meta property=\"og:title\" content=\"Pedido #").Append(order.Number).Append(" · ").Append(E(company.Name)).Append("\"><meta property=\"og:description\" content=\"Acompanhe a etapa e a previsão de entrega do seu pedido.\"><meta property=\"og:site_name\" content=\"").Append(E(company.Name)).Append("\">");
        // Logotipo na prévia do WhatsApp (precisa de PNG com endereço completo).
        if (publicRoot != null) sb.Append("<meta property=\"og:image\" content=\"").Append(E(publicRoot)).Append("/p/").Append(PreviewImageFile).Append("\">");
        sb.Append("<title>Pedido #").Append(order.Number).Append(" · ").Append(E(company.Name)).Append("</title><style>").Append(Style).Append("</style></head><body>");
        sb.Append(Header(company)).Append("<main>");
        sb.Append("<section class=\"card\"><div class=\"muted\">Pedido</div><h1>#").Append(order.Number).Append(" · ").Append(E(FirstName(order.CustomerName))).Append("</h1><div class=\"muted\">");
        var items = order.Items.Count > 0 ? order.Items.Count : 1;
        sb.Append(items).Append(items == 1 ? " item" : " itens");
        if (order.DeadlineStarted && order.Stage is not (OrderStages.Ready or OrderStages.Delivered)) sb.Append(" · previsão de entrega <b>").Append(order.DueDate.ToString("dd/MM/yyyy")).Append("</b>");
        else if (!order.DeadlineStarted) sb.Append(" · a previsão começa quando a arte for aprovada");
        sb.Append("</div></section>");
        sb.Append("<section class=\"card\"><b>Andamento</b><ol>");
        for (var i = 0; i < Steps.Length; i++)
        {
            var css = i < current || order.Stage == OrderStages.Delivered ? "done" : i == current ? "now" : "";
            var label = Steps[i] == OrderStages.Approval ? "Aprovação da arte" : Steps[i];
            sb.Append("<li class=\"").Append(css).Append("\"><i>").Append(css == "done" ? "✓" : "").Append("</i>").Append(E(label)).Append("</li>");
        }
        sb.Append("</ol></section>");
        if (order.Stage == OrderStages.Ready) sb.Append("<section class=\"card\"><b>Seu pedido está pronto para retirada.</b><div class=\"muted\">").Append(E(company.Address)).Append("</div>").Append(HoursBlock(company)).Append("</section>");
        if (showBalance && !order.PricePending && order.RemainingAmount > 0)
        {
            sb.Append("<section class=\"card\"><div class=\"muted\">Valor a pagar na retirada</div><div class=\"big\">").Append(E(order.RemainingAmount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")))).Append("</div>");
            // Pix copia e cola com o saldo (sem scripts: um toque seleciona o código inteiro para copiar).
            if (PixBrCode.NormalizeKey(company.PixKey).Length > 0 && PixBrCode.Validate(company.PixKey) == null)
                sb.Append("<div class=\"muted pixlabel\">Quer adiantar? Pague por Pix: toque no código, copie e cole no app do banco.</div><div class=\"pix\">")
                  .Append(E(PixBrCode.Build(company.PixKey, company.Name, company.City, order.RemainingAmount, "PEDIDO" + order.Number))).Append("</div>");
            sb.Append("</section>");
        }
        // Depois de entregue: convite para avaliar no Google (só com o link configurado em Sistema › Configurações › Empresa).
        if (order.Stage == OrderStages.Delivered && ReviewLink(company.ReviewUrl) is string review)
            sb.Append("<section class=\"card center\"><b>Obrigado pela preferência!</b><div class=\"muted\">Sua opinião ajuda a gráfica a crescer.</div><a class=\"btn star\" rel=\"noopener\" href=\"").Append(E(review)).Append("\">★ Avaliar no Google</a></section>");
        var phone = CommunicationOperations.WhatsAppNumber(company.Phone ?? "");
        if (phone.Length >= 12) sb.Append("<a class=\"btn\" rel=\"noopener\" href=\"https://wa.me/").Append(phone).Append("\">Falar com a gráfica no WhatsApp</a>");
        sb.Append("<div class=\"foot\">");
        if (order.Stage != OrderStages.Ready && !string.IsNullOrWhiteSpace(company.Address)) sb.Append(E(company.Address)).Append("<br>");
        if (order.Stage != OrderStages.Ready && HoursLines(company).Length > 0) sb.Append(string.Join("<br>", HoursLines(company).Select(E))).Append("<br>");
        sb.Append(E(company.Name)).Append("</div>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    public static string NotFoundPage(CompanyProfile company) =>
        "<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\"><title>Pedido</title><style>" + Style + "</style></head><body>" + Header(company) + "<main><section class=\"card\"><b>Este link não está mais disponível.</b><div class=\"muted\">Fale com a gráfica para saber do seu pedido.</div></section></main></body></html>";
}
