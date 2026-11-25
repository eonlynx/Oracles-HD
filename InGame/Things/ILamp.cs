namespace ProjectZ.InGame.Things
{
    /// <summary>
    /// Shared on/off contract for native lamps and Oracle torch hosts. The
    /// room darkener must not depend on the concrete lamp class.
    /// </summary>
    public interface ILamp
    {
        bool IsOn();
    }
}
