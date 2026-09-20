using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed class UIWindowRecord
    {
        internal readonly UIMetadata Metadata;
        internal UIWindow View;
        internal UIWindowLoad Flight;
        internal int LayerIndex = -1;
        internal int CacheIndex = -1;
        internal ulong CacheTimer;
        internal bool ForceClose;
        internal UniTaskCompletionSource Settled;
        internal bool IsCached => CacheIndex >= 0;
        internal UIState State => View?.State ?? UIState.Uninitialized;
        internal UIMetaRegistry.UIMetaInfo MetaInfo => Metadata.MetaInfo;

        internal UIWindowRecord(UIMetadata metadata) => Metadata = metadata;
    }

    internal sealed class UIWindowLoad
    {
        internal readonly UIWindow View;
        internal readonly CancellationTokenSource Cancellation;
        internal readonly List<UIOpenRequest> Requests = new();
        internal int Waiters;

        internal UIWindowLoad(UIWindow view)
        {
            View = view;
            Cancellation = new CancellationTokenSource();
        }

        internal void Cancel() => UIHolderObjectBase.Cancel(Cancellation);
    }

    internal sealed class UIOpenRequest
    {
        internal readonly object[] Arguments;
        internal readonly CancellationToken Token;
        internal readonly UniTaskCompletionSource<UIOpenResult> Completion = new();
        internal CancellationTokenRegistration Registration;
        internal bool Completed => Completion.Task.Status.IsCompleted();

        internal UIOpenRequest(object[] arguments, CancellationToken token)
        {
            Arguments = arguments;
            Token = token;
        }

        internal void Finish(UIOpenResult result)
        {
            Registration.Dispose();
            Completion.TrySetResult(result);
        }

    }

    internal enum UIOpenStatus : byte { Failed, Opened, Cancelled, Ignored }

    internal readonly struct UIOpenResult
    {
        internal readonly UIWindow View;
        internal readonly UIOpenStatus Status;

        internal UIOpenResult(UIWindow view, UIOpenStatus status)
        {
            View = view;
            Status = status;
        }

        internal static UIOpenResult Failed => new(null, UIOpenStatus.Failed);
        internal static UIOpenResult Cancelled => new(null, UIOpenStatus.Cancelled);
    }
}
