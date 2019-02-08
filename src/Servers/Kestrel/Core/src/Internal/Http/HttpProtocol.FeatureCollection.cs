// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class HttpProtocol : IHttpRequestFeature,
                                        IHttpResponseFeature,
                                        IHttpUpgradeFeature,
                                        IHttpConnectionFeature,
                                        IHttpRequestLifetimeFeature,
                                        IHttpRequestIdentifierFeature,
                                        IHttpBodyControlFeature,
                                        IHttpMaxRequestBodySizeFeature,
                                        IHttpResponseStartFeature,
                                        IResponseBodyPipeFeature
    {
        // NOTE: When feature interfaces are added to or removed from this HttpProtocol class implementation,
        // then the list of `implementedFeatures` in the generated code project MUST also be updated.
        // See also: tools/CodeGenerator/HttpProtocolFeatureCollection.cs

        string IHttpRequestFeature.Protocol
        {
            get => HttpVersion;
            set => HttpVersion = value;
        }

        string IHttpRequestFeature.Scheme
        {
            get => Scheme ?? "http";
            set => Scheme = value;
        }

        string IHttpRequestFeature.Method
        {
            get
            {
                if (_methodText != null)
                {
                    return _methodText;
                }

                _methodText = HttpUtilities.MethodToString(Method) ?? string.Empty;
                return _methodText;
            }
            set
            {
                _methodText = value;
            }
        }

        string IHttpRequestFeature.PathBase
        {
            get => PathBase ?? "";
            set => PathBase = value;
        }

        string IHttpRequestFeature.Path
        {
            get => Path;
            set => Path = value;
        }

        string IHttpRequestFeature.QueryString
        {
            get => QueryString;
            set => QueryString = value;
        }

        string IHttpRequestFeature.RawTarget
        {
            get => RawTarget;
            set => RawTarget = value;
        }

        IHeaderDictionary IHttpRequestFeature.Headers
        {
            get => RequestHeaders;
            set => RequestHeaders = value;
        }

        Stream IHttpRequestFeature.Body
        {
            get => RequestBody;
            set => RequestBody = value;
        }

        int IHttpResponseFeature.StatusCode
        {
            get => StatusCode;
            set => StatusCode = value;
        }

        string IHttpResponseFeature.ReasonPhrase
        {
            get => ReasonPhrase;
            set => ReasonPhrase = value;
        }

        IHeaderDictionary IHttpResponseFeature.Headers
        {
            get => ResponseHeaders;
            set => ResponseHeaders = value;
        }

        CancellationToken IHttpRequestLifetimeFeature.RequestAborted
        {
            get => RequestAborted;
            set => RequestAborted = value;
        }

        bool IHttpResponseFeature.HasStarted => HasResponseStarted;

        bool IHttpUpgradeFeature.IsUpgradableRequest => IsUpgradableRequest;

        IPAddress IHttpConnectionFeature.RemoteIpAddress
        {
            get => RemoteIpAddress;
            set => RemoteIpAddress = value;
        }

        IPAddress IHttpConnectionFeature.LocalIpAddress
        {
            get => LocalIpAddress;
            set => LocalIpAddress = value;
        }

        int IHttpConnectionFeature.RemotePort
        {
            get => RemotePort;
            set => RemotePort = value;
        }

        int IHttpConnectionFeature.LocalPort
        {
            get => LocalPort;
            set => LocalPort = value;
        }

        string IHttpConnectionFeature.ConnectionId
        {
            get => ConnectionIdFeature;
            set => ConnectionIdFeature = value;
        }

        string IHttpRequestIdentifierFeature.TraceIdentifier
        {
            get => TraceIdentifier;
            set => TraceIdentifier = value;
        }

        bool IHttpBodyControlFeature.AllowSynchronousIO
        {
            get => AllowSynchronousIO;
            set => AllowSynchronousIO = value;
        }

        bool IHttpMaxRequestBodySizeFeature.IsReadOnly => HasStartedConsumingRequestBody || IsUpgraded;

        long? IHttpMaxRequestBodySizeFeature.MaxRequestBodySize
        {
            get => MaxRequestBodySize;
            set
            {
                if (HasStartedConsumingRequestBody)
                {
                    throw new InvalidOperationException(CoreStrings.MaxRequestBodySizeCannotBeModifiedAfterRead);
                }
                if (IsUpgraded)
                {
                    throw new InvalidOperationException(CoreStrings.MaxRequestBodySizeCannotBeModifiedForUpgradedRequests);
                }
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.NonNegativeNumberOrNullRequired);
                }

                MaxRequestBodySize = value;
            }
        }

        PipeWriter IResponseBodyPipeFeature.ResponseBodyPipe
        {
            get
            {
                return ResponsePipeWriter;
            }
            set
            {
                ResponsePipeWriter = value;

                if (!object.ReferenceEquals(_cachedResponsePipeWriter, ResponsePipeWriter))
                {
                    var responseBody = new WriteOnlyPipeStream(ResponsePipeWriter);
                    ResponseBody = responseBody;

                    RegisterWrapperForDisposal(responseBody);
                }
            }
        }

        Stream IHttpResponseFeature.Body
        {
            get
            {
                return ResponseBody;
            }
            set
            {
                ResponseBody = value;
                if (!object.ReferenceEquals(_cachedResponseBodyStream, ResponseBody))
                {
                    var responsePipeWriter = new StreamPipeWriter(ResponseBody, minimumSegmentSize: 4096, _context.MemoryPool);
                    ResponsePipeWriter = responsePipeWriter;
                    _cachedResponseBodyStream = ResponseBody;
                    RegisterWrapperForDisposal(responsePipeWriter);
                }
            }
        }

        private void RegisterWrapperForDisposal(IDisposable responseBody)
        {
            if (_wrapperObjectsToDispose == null)
            {
                _wrapperObjectsToDispose = new List<IDisposable>();
            }
            _wrapperObjectsToDispose.Add(responseBody);
        }

        protected void ResetHttp1Features()
        {
            _currentIHttpMinRequestBodyDataRateFeature = this;
            _currentIHttpMinResponseDataRateFeature = this;
        }

        protected void ResetHttp2Features()
        {
            _currentIHttp2StreamIdFeature = this;
            _currentIHttpResponseTrailersFeature = this;
        }

        void IHttpResponseFeature.OnStarting(Func<object, Task> callback, object state)
        {
            OnStarting(callback, state);
        }

        void IHttpResponseFeature.OnCompleted(Func<object, Task> callback, object state)
        {
            OnCompleted(callback, state);
        }

        async Task<Stream> IHttpUpgradeFeature.UpgradeAsync()
        {
            if (!IsUpgradableRequest)
            {
                throw new InvalidOperationException(CoreStrings.CannotUpgradeNonUpgradableRequest);
            }

            if (IsUpgraded)
            {
                throw new InvalidOperationException(CoreStrings.UpgradeCannotBeCalledMultipleTimes);
            }

            if (!ServiceContext.ConnectionManager.UpgradedConnectionCount.TryLockOne())
            {
                throw new InvalidOperationException(CoreStrings.UpgradedConnectionLimitReached);
            }

            IsUpgraded = true;

            ConnectionFeatures.Get<IDecrementConcurrentConnectionCountFeature>()?.ReleaseConnection();

            StatusCode = StatusCodes.Status101SwitchingProtocols;
            ReasonPhrase = "Switching Protocols";
            ResponseHeaders["Connection"] = "Upgrade";

            await FlushAsync();

            return _streams.Upgrade();
        }

        void IHttpRequestLifetimeFeature.Abort()
        {
            ApplicationAbort();
        }


        Task IHttpResponseStartFeature.StartAsync(CancellationToken cancellationToken)
        {
            if (HasResponseStarted)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return InitializeResponseAsync(0);
        }
    }
}
