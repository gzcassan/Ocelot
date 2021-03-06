﻿using System;
using System.Threading.Tasks;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Logging;
using Ocelot.Middleware;

namespace Ocelot.LoadBalancer.Middleware
{
    public class LoadBalancingMiddleware : OcelotMiddleware
    {
        private readonly OcelotRequestDelegate _next;
        private readonly IOcelotLogger _logger;
        private readonly ILoadBalancerHouse _loadBalancerHouse;

        public LoadBalancingMiddleware(OcelotRequestDelegate next,
            IOcelotLoggerFactory loggerFactory,
            ILoadBalancerHouse loadBalancerHouse) 
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<LoadBalancingMiddleware>();
            _loadBalancerHouse = loadBalancerHouse;
        }

        public async Task Invoke(DownstreamContext context)
        {
            var loadBalancer = await _loadBalancerHouse.Get(context.DownstreamReRoute, context.ServiceProviderConfiguration);
            if(loadBalancer.IsError)
            {
                _logger.LogDebug("there was an error retriving the loadbalancer, setting pipeline error");
                SetPipelineError(context, loadBalancer.Errors);
                return;
            }

            var hostAndPort = await loadBalancer.Data.Lease();
            if(hostAndPort.IsError)
            {
                _logger.LogDebug("there was an error leasing the loadbalancer, setting pipeline error");
                SetPipelineError(context, hostAndPort.Errors);
                return;
            }

            context.DownstreamRequest.Host = hostAndPort.Data.DownstreamHost;

            if (hostAndPort.Data.DownstreamPort > 0)
            {
                context.DownstreamRequest.Port = hostAndPort.Data.DownstreamPort;
            }

            try
            {
                await _next.Invoke(context);
            }
            catch (Exception)
            {
                _logger.LogDebug("Exception calling next middleware, exception will be thrown to global handler");
                throw;
            }
            finally
            {
                loadBalancer.Data.Release(hostAndPort.Data);
            }
        }
    }
}
