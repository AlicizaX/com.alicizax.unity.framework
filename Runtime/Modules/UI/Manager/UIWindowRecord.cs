using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal sealed class UIWindowRecord
    {
        internal readonly UIMetadata Metadata;
        internal UIBase View;
        internal UIWindowLoad Flight;
        internal int LayerIndex = -1;
        internal int CacheIndex = -1;
        internal ulong CacheTimer;
        internal bool ForceClose;
        internal UIState State => View?.State ?? UIState.Uninitialized;
        internal UIMetaRegistry.UIMetaInfo MetaInfo => Metadata.MetaInfo;

        internal UIWindowRecord(UIMetadata metadata) => Metadata = metadata;
    }

    internal sealed class UIWindowLoad
    {
        internal readonly UIBase View;
        internal readonly CancellationTokenSource Cancellation;
        internal readonly List<UIOpenRequest> Requests = new();
        internal int Waiters;

        internal UIWindowLoad(UIBase view)
        {
            View = view;
            Cancellation = view.BeginResourceLoad();
        }
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

    internal enum UIOpenStatus : byte { Failed, Opened, Cancelled }

    internal readonly struct UIOpenResult
    {
        internal readonly UIBase View;
        internal readonly UIOpenStatus Status;
        internal readonly UniTask Transition;

        internal UIOpenResult(UIBase view, UIOpenStatus status)
        {
            View = view;
            Status = status;
            Transition = view?.AwaitTransition() ?? UniTask.CompletedTask;
        }

        internal static UIOpenResult Failed => new(null, UIOpenStatus.Failed);
        internal static UIOpenResult Cancelled => new(null, UIOpenStatus.Cancelled);
    }
}
