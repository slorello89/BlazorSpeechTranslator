using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nexmo.Api.Voice.Nccos;
using Nexmo.Api.Voice.Nccos.Endpoints;
using SpeechTranslatorBlazor.Shared;

namespace SpeechTranslatorBlazor.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VoiceController : ControllerBase
    {
        [Route("/webhooks/answer")]
        [HttpGet]
        public ActionResult Answer()
        {
            var host = Request.Host.ToString();            
            var webSocketAction = new ConnectAction()
            {
                Endpoint = new[]
                {
                    new WebsocketEndpoint()
                    {
                        Uri = $"ws://{host}/ws",
                        ContentType="audio/l16;rate=16000",
                        Headers = new Translation
                        {
                            UUID = Request.Query["uuid"].ToString(), 
                            LanguageSpoken = "en-US", 
                            LanguageTranslated = "hi-IN"
                        }
                    }
                }
            };
            var ncco = new Ncco(webSocketAction);
            return Ok(ncco.ToString());
        }
    }
}
