using System;

namespace Butler.OnePasswordExtensions;

public class OnePasswordSetupException : Exception
{
    public OnePasswordSetupException() : base()
	{
	}
	
	public OnePasswordSetupException(string message) : base(message)
	{
	}
	
	public OnePasswordSetupException(string message, Exception innerException) : base(message, innerException)
	{
	}
}
