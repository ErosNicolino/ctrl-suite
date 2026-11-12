using System;
using System.Collections.Generic;
using System.Linq;

namespace GraficaCaixa;

public static class CommunicationOperations
{
	public const string BudgetFollowUpTemplateName = "Retorno do orçamento";
	public const string CollectionTemplateName = "Cobrança de devedores",ReadyTemplateName = "Pedido pronto", PickupTemplateName = "Lembrete de retirada", ReceiptTemplateName = "Envio de recibo", BudgetTemplateName = "Envio de orçamento", TrackingTemplateName = "Link de acompanhamento";

	// Modelos salvos (ativos) + os padrões que ainda não foram salvos com o mesmo nome. Salvar um padrão não some com os outros.
	public static IReadOnlyList<CommunicationTemplate> EffectiveTemplates(Prompt12State state)
	{
		var saved = state.CommunicationTemplates ?? new List<CommunicationTemplate>();
		var names = saved.Select(x => x.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return saved.Where(x => x.Active).Concat(DefaultTemplates().Where(x => !names.Contains(x.Name))).ToList();
	}

	public static CommunicationTemplate[] DefaultTemplates()
	{
		return new CommunicationTemplate[9]
		{
			new CommunicationTemplate
			{
				Name = "Orçamento",
				Body = "Olá {cliente}, segue o orçamento do pedido {pedido}, total {total}. Prazo: {prazo}."
			},
			new CommunicationTemplate
			{
				Name = ReadyTemplateName,
				Body = "Olá {cliente}, o pedido {pedido} está pronto para retirada. Saldo: {saldo}.{pix}"
			},
			new CommunicationTemplate
			{
				Name = "Cobrança",
				Body = "Olá {cliente}, lembramos que o pedido {pedido} possui saldo de {saldo}.{pix}"
			},
			// Usado pelo botão "Copiar cobrança" de Devedores (pedidos e dívidas avulsas).
			new CommunicationTemplate
			{
				Name = CollectionTemplateName,
				Body = "Olá, {cliente}, tudo bem?\n\nIdentificamos um valor pendente de *{saldo}*{referencia}.{pix}\n\nQuando possível, pedimos a gentileza de verificar essa pendência.\n\nCaso o pagamento já tenha sido realizado, por favor desconsidere esta mensagem e, se possível, nos envie o comprovante para conferência.\n\nObrigado!\n*{grafica}*"
			},
			// Usado pelo "Avisar retiradas" de Pedidos: pedidos prontos há alguns dias (Regras > Pedidos).
			new CommunicationTemplate
			{
				Name = PickupTemplateName,
				Body = "Olá, {cliente}, tudo bem? Seu pedido {pedido} está pronto para retirada desde {pronto}. Estamos te esperando!{pix}\n\n*{grafica}*"
			},
			// Texto que acompanha o PDF enviado pelo WhatsApp (recibo do pedido / recibo avulso e prévia de orçamento).
			new CommunicationTemplate
			{
				Name = ReceiptTemplateName,
				Body = "Olá, {cliente}! Segue o recibo em PDF. Obrigado pela preferência!\n\n*{grafica}*"
			},
			new CommunicationTemplate
			{
				Name = BudgetTemplateName,
				Body = "Olá, {cliente}! Segue o orçamento em PDF, no total de {total}. Qualquer dúvida, estamos à disposição.\n\n*{grafica}*"
			},
			// Botão WhatsApp de Orçamentos em acompanhamento: cobra o retorno do orçamento enviado.
			new CommunicationTemplate
			{
				Name = BudgetFollowUpTemplateName,
				Body = "Olá, {cliente}, tudo bem? Conseguiu ver o orçamento de {referencia}{total}? Se quiser, já colocamos em produção.\n\n*{grafica}*"
			},
			// Botão "Link de acompanhamento" de Pedidos ({link} = página do pedido).
			new CommunicationTemplate
			{
				Name = TrackingTemplateName,
				Body = "Olá, {cliente}! Tudo bem?\n\nAcompanhe o seu pedido *{pedido}* por este link:\n{link}\n\nLá você vê a etapa atual e a previsão de entrega. Quando ficar pronto, aparecem o endereço e o horário para retirada.\n\nQualquer dúvida, é só responder esta mensagem.\n*{grafica}*"
			}
		};
	}

	// Mensagem do link de acompanhamento (modelo "Link de acompanhamento"; o link sempre vai junto, mesmo se o modelo não tiver {link}).
	public static string TrackingMessage(Prompt12State? state, CompanyProfile company, string customer, long number, string url)
	{
		var template = EffectiveTemplates(state ?? new Prompt12State()).FirstOrDefault(x => x.Name.Trim().Equals(TrackingTemplateName, StringComparison.OrdinalIgnoreCase))?.Body
			?? DefaultTemplates().First(x => x.Name == TrackingTemplateName).Body;
		var text = MessageTemplates.Render(template, new Dictionary<string, string> { ["cliente"] = (customer ?? "").Trim(), ["pedido"] = number > 0 ? $"#{number}" : "", ["link"] = url, ["grafica"] = company?.Name ?? "" });
		return text.Contains(url, StringComparison.Ordinal) ? text : text + "\n\n" + url;
	}

	// Número para o WhatsApp (único lugar que decide): 10 ou 11 dígitos ganham o 55; com "+" na frente, vale o código do país digitado.
	public static string WhatsAppNumber(string phone)
	{
		var value = phone ?? "";
		var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
		if (!value.TrimStart().StartsWith('+') && digits.Length is 10 or 11) digits = "55" + digits;
		if (digits.Length < 10 || digits.Length > 15 || digits.StartsWith('0')) throw new InvalidOperationException("Informe o telefone completo com DDD. Para outro país, use + e o código do país. Um final como 7390 não é suficiente.");
		return digits;
	}

	public static bool IsWhatsAppNumber(string? phone)
	{
		try { WhatsAppNumber(phone ?? ""); return true; } catch (InvalidOperationException) { return false; }
	}

	public static string WhatsAppUrl(string phone, string text)
	{
		return "https://wa.me/" + WhatsAppNumber(phone) + (text.Length > 0 ? "?text=" + Uri.EscapeDataString(text) : "");
	}

	public static string MailTo(string email, string subject, string body)
	{
		return "mailto:" + email + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body);
	}
}
