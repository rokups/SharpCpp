namespace SharpCpp.Support;

public interface INativeObjectRegistration<TSelf>
    where TSelf : NativeObject
{
    static abstract nuint RegistrationDynamicTypeId { get; }

    static abstract Type RegistrationExpectedType { get; }

    static abstract INativeInstanceCache RegistrationInstanceCache { get; }

    static abstract Func<nint, bool, NativeObject>? RegistrationCreateFactory { get; }

    static abstract NativeObjectMetadata RegistrationGetMetadata(nint handle);
}
