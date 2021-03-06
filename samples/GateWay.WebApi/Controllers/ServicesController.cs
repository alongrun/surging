﻿using Microsoft.AspNetCore.Mvc;
using Surging.Core.ApiGateWay;
using Surging.Core.ApiGateWay.OAuth;
using Surging.Core.CPlatform;
using Surging.Core.CPlatform.Filters.Implementation;
using Surging.Core.CPlatform.Routing;
using Surging.Core.ProxyGenerator;
using Surging.Core.ProxyGenerator.Utilitys;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using GateWayAppConfig = Surging.Core.ApiGateWay.AppConfig;
using System.Reflection;
using Surging.Core.CPlatform.Utilities;

namespace GateWay.WebApi
{
    /// <summary>
    /// 身份认证
    /// </summary>
    [Route("api/[controller]")]
    public class AuthController : Controller
    {
        private readonly IServiceProxyProvider _serviceProxyProvider;
        private readonly IServiceRouteProvider _serviceRouteProvider;
        private readonly IAuthorizationServerProvider _authorizationServerProvider;


        public AuthController()
        /*  IServiceProxyProvider serviceProxyProvider, 
      IServiceRouteProvider serviceRouteProvider,
           IAuthorizationServerProvider authorizationServerProvider)
           */
        {
            // ServiceLocator.GetService<IServiceProxyProvider>();  //
            //_serviceProxyProvider = serviceProxyProvider;
            //_serviceRouteProvider =  serviceRouteProvider;
            //_authorizationServerProvider =  authorizationServerProvider;

            _serviceProxyProvider = ServiceLocator.GetService<IServiceProxyProvider>();
            _serviceRouteProvider = ServiceLocator.GetService<IServiceRouteProvider>(); //serviceRouteProvider;
            _authorizationServerProvider = ServiceLocator.GetService<IAuthorizationServerProvider>();// authorizationServerProvider;


        }

        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="path"></param>
        /// <param name="serviceKey"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ServiceResult<object>> Post(string path, string serviceKey, Dictionary<string, object> model)
        {
            model = model ?? new Dictionary<string, object>();
            ServiceResult<object> result = ServiceResult<object>.Create(false, null);
            path = string.IsNullOrEmpty(path) ?
                GateWayAppConfig.AuthorizationRoutePath : path.ToLower();
            if (OnAuthorization(path, model, ref result))
            {
                if (path == GateWayAppConfig.AuthorizationRoutePath)
                {
                    var token = await _authorizationServerProvider.GenerateTokenCredential(model);
                    if (token != null)
                    {
                        result = ServiceResult<object>.Create(true, token);
                        result.StatusCode = (int)ServiceStatusCode.Success;
                    }
                    else
                    {
                        result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.AuthorizationFailed, Message = "Invalid authentication credentials" };
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(serviceKey))
                    {

                        result = ServiceResult<object>.Create(true, await _serviceProxyProvider.Invoke<object>(model, path, serviceKey));
                        result.StatusCode = (int)ServiceStatusCode.Success;
                    }
                    else
                    {
                        result = ServiceResult<object>.Create(true, await _serviceProxyProvider.Invoke<object>(model, path));
                        result.StatusCode = (int)ServiceStatusCode.Success;
                    }
                }
            }
            return result;
        }

        private bool OnAuthorization(string path, Dictionary<string, object> model, ref ServiceResult<object> result)
        {
            bool isSuccess = true;
            var route = _serviceRouteProvider.GetRouteByPath(path).Result;
            if (route.ServiceDescriptor.EnableAuthorization())
            {
                if (route.ServiceDescriptor.AuthType() == AuthorizationType.JWT.ToString())
                {
                    isSuccess = ValidateJwtAuthentication(route, model, ref result);
                }
                else
                {
                    isSuccess = ValidateAppSecretAuthentication(route, path, model, ref result);
                }

            }
            return isSuccess;
        }

        public bool ValidateJwtAuthentication(ServiceRoute route, Dictionary<string, object> model, ref ServiceResult<object> result)
        {
            bool isSuccess = true;
            var author = HttpContext.Request.Headers["Authorization"];
            if (author.Count > 0)
            {
                if (route.Address.Any(p => p.DisableAuth == false))
                {
                    isSuccess = _authorizationServerProvider.ValidateClientAuthentication(author).Result;
                    if (!isSuccess)
                    {
                        result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.AuthorizationFailed, Message = "Invalid authentication credentials" };
                    }
                    else
                    {
                        var keyValue = model.FirstOrDefault();
                        if (!(keyValue.Value is IConvertible) || !typeof(IConvertible).GetTypeInfo().IsAssignableFrom(keyValue.Value.GetType()))
                        {
                            dynamic instance = keyValue.Value;
                            instance.Payload = _authorizationServerProvider.GetPayloadString(author);
                            model.Remove(keyValue.Key);
                            model.Add(keyValue.Key, instance);
                        }
                    }
                }
            }
            else
            {
                result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.RequestError, Message = "Request error" };
                isSuccess = false;
            }
            return isSuccess;
        }

        private bool ValidateAppSecretAuthentication(ServiceRoute route, string path,
            Dictionary<string, object> model, ref ServiceResult<object> result)
        {
            bool isSuccess = true;
            DateTime time;
            var author = HttpContext.Request.Headers["Authorization"];
            if (route.Address.Any(p => p.DisableAuth == false))
            {
                if (!string.IsNullOrEmpty(path) && model.ContainsKey("timeStamp") && author.Count > 0)
                {
                    if (DateTime.TryParse(model["timeStamp"].ToString(), out time))
                    {
                        var seconds = (DateTime.Now - time).TotalSeconds;
                        if (seconds <= 3560 && seconds >= 0)
                        {
                            if (!route.Address.Any(p => GetMD5($"{p.Token}{time.ToString("yyyy-MM-dd hh:mm:ss") }") == author.ToString()))
                            {
                                result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.AuthorizationFailed, Message = "Invalid authentication credentials" };
                                isSuccess = false;
                            }
                        }
                        else
                        {
                            result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.AuthorizationFailed, Message = "Invalid authentication credentials" };
                            isSuccess = false;
                        }
                    }
                    else
                    {
                        result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.AuthorizationFailed, Message = "Invalid authentication credentials" };
                        isSuccess = false;
                    }
                }
                else
                {
                    result = new ServiceResult<object> { IsSucceed = false, StatusCode = (int)ServiceStatusCode.RequestError, Message = "Request error" };
                    isSuccess = false;
                }
            }
            return isSuccess;
        }

        public static string GetMD5(string encypStr)
        {
            try
            {
                var md5 = MD5.Create();
                var bs = md5.ComputeHash(Encoding.UTF8.GetBytes(encypStr));
                var sb = new StringBuilder();
                foreach (byte b in bs)
                {
                    sb.Append(b.ToString("X2"));
                }
                //所有字符转为大写
                return sb.ToString().ToLower();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.StackTrace);
                return null;
            }
        }
    }
}
