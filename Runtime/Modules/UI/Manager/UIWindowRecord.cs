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
        internal UIWindowRecord StackBelow;
        internal UIWindowRecord StackAbove;
        internal int Order = -1;
        internal int CacheIndex = -1;
        internal ulong CacheTimer;
        internal bool IsStacked => Order >= 0;
        internal bool IsCached => CacheIndex >= 0;
        internal UIState State => View?.State ?? UIState.Uninitialized;
        internal ref readonly UIMetaRegistry.UIMetaInfo MetaInfo => ref Metadata.MetaInfo;

        internal UIWindowRecord(UIMetadata metadata) => Metadata = metadata;
    }

    internal sealed class UIWindowLoad
    {
        internal readonly UIWindowRecord Record;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly List<UIOpenRequest> Requests = new();

        internal UIWindowLoad(UIWindowRecord record) => Record = record;

        internal bool HasWaiter() => HasWaiterOtherThan(null);

        internal bool HasWaiterOtherThan(UIOpenRequest excluded)
        {
            for (int i = 0; i < Requests.Count; i++)
            {
                UIOpenRequest request = Requests[i];
                if (request != excluded && !request.Completed && !request.Token.IsCancellationRequested) return true;
            }
            return false;
        }

        internal object[] LatestArguments()
        {
            object[] arguments = null;
            for (int i = 0; i < Requests.Count; i++)
            {
                UIOpenRequest request = Requests[i];
                if (request.Completed || request.Token.IsCancellationRequested) continue;
                if (request.Arguments != null && request.Arguments.Length > 0) arguments = request.Arguments;
            }
            return arguments;
        }

        internal void Finish(UIOpenResult result)
        {
            for (int i = 0; i < Requests.Count; i++)
            {
                UIOpenRequest request = Requests[i];
                if (!request.Completed) request.Finish(result);
            }
            Requests.Clear();
        }
    }

    internal sealed class UIOpenRequest
    {
        internal readonly UIWindowLoad Flight;
        internal readonly object[] Arguments;
        internal readonly CancellationToken Token;
        internal readonly UniTaskCompletionSource<UIOpenResult> Completion = new();
        internal CancellationTokenRegistration Registration;
        internal bool Completed => Completion.Task.Status.IsCompleted();

        internal UIOpenRequest(UIWindowLoad flight, object[] arguments, CancellationToken token)
        {
            Flight = flight;
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
