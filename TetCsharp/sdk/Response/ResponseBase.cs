﻿/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */

using System;
using Newtonsoft.Json;

namespace EyeTribe.ClientSdk.Response
{
    public class ResponseBase
    {
        [JsonProperty(PropertyName = Protocol.KEY_CATEGORY)]
        public string Category { set; get; }

        [JsonProperty(PropertyName = Protocol.KEY_REQUEST)]
        public string Request { set; get; }

        [JsonProperty(PropertyName = Protocol.KEY_STATUSCODE)]
        public int StatusCode { set; get; }

        [JsonProperty(PropertyName = Protocol.KEY_ID)]
        public int Id { set; get; }

        [JsonIgnore]
        public long TransitTime;
    }
}
