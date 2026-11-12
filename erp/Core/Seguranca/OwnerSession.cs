using System;

namespace GraficaCaixa;

public sealed class OwnerSession
{
	public Guid Token { get; } = Guid.NewGuid();

	public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

	public TimeSpan IdleTimeout { get; }

	public bool IsActive => DateTime.UtcNow - LastActivityUtc <= IdleTimeout;

	internal OwnerSession(TimeSpan idleTimeout)
	{
		IdleTimeout = idleTimeout;
	}

	public void Touch()
	{
		if (!IsActive)
		{
			throw new UnauthorizedAccessException("Sessão do proprietário expirada.");
		}
		LastActivityUtc = DateTime.UtcNow;
	}

	public void Lock()
	{
		LastActivityUtc = DateTime.MinValue;
	}
}
