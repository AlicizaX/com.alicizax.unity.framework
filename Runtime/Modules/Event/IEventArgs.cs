namespace AlicizaX
{
    public delegate void InEventHandler<T>(in T evt) where T : struct, IPayloadEventArgs;

    public interface IPayloadEventArgs
    {
    }

    public interface IEmptyEventArgs
    {
    }
}
