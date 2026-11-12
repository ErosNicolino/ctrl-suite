using System;

namespace GraficaCaixa;

public sealed class OwnerPinCredential
{
	public byte[] Salt { get; set; } = Array.Empty<byte>();

	public byte[] Hash { get; set; } = Array.Empty<byte>();

	public int Iterations { get; set; } = 210000;
}
