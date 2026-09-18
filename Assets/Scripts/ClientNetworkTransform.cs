using Unity.Netcode.Components;

// Owner-authoritative transform sync: every player moves their own character locally
// (responsive controls) and the position is replicated to everybody else.
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
