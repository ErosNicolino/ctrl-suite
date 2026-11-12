using System;

namespace GraficaCaixa;

public sealed class CommunicationTemplate
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "";

	public string Channel { get; set; } = "WhatsApp";

	public string Subject { get; set; } = "";

	public string Body { get; set; } = "";

	public bool Active { get; set; } = true;
}
