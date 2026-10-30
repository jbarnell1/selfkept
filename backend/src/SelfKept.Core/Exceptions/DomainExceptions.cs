namespace SelfKept.Core.Exceptions;

/// <summary>A business-rule violation the caller can correct. Mapped to 400 with its message.</summary>
public class DomainValidationException : Exception
{
  public DomainValidationException(string message) : base(message) { }
}

/// <summary>The caller is signed in but may not do this. Mapped to 403.</summary>
public class ForbiddenException : Exception
{
  public ForbiddenException(string message = "You do not have permission to perform this action.") : base(message) { }
}

/// <summary>Sign-up is closed and no valid invite was presented. Mapped to 403.</summary>
public class RegistrationClosedException : ForbiddenException
{
  public RegistrationClosedException(string message) : base(message) { }
}

/// <summary>The resource already exists (for example an email that is already registered). Mapped to 409.</summary>
public class ConflictException : Exception
{
  public ConflictException(string message) : base(message) { }
}
