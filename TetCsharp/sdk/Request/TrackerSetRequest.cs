﻿/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */

using Newtonsoft.Json;
using EyeTribe.ClientSdk.Response;

namespace EyeTribe.ClientSdk.Request
{
    internal class TrackerSetRequest : RequestBase<ResponseBase>
    {
        public TrackerSetRequest()
        {
            Values = new TrackerSetRequestValues();

            this.Category = Protocol.CATEGORY_TRACKER;
            this.Request = Protocol.TRACKER_REQUEST_SET;
        }

        [JsonProperty(PropertyName = Protocol.KEY_VALUES)]
        public TrackerSetRequestValues Values { set; get; }
    }

    internal class TrackerSetRequestValues
    {
        [JsonProperty(PropertyName = Protocol.TRACKER_VERSION, NullValueHandling = NullValueHandling.Ignore)]
        public int? Version { set; get; }

        [JsonProperty(PropertyName = Protocol.TRACKER_SCREEN_INDEX, NullValueHandling = NullValueHandling.Ignore)]
        public int? ScreenIndex { set; get; }

        [JsonProperty(PropertyName = Protocol.TRACKER_SCREEN_RESOLUTION_WIDTH, NullValueHandling = NullValueHandling.Ignore)]
        public int? ScreenResolutionWidth { set; get; }

        [JsonProperty(PropertyName = Protocol.TRACKER_SCREEN_RESOLUTION_HEIGHT, NullValueHandling = NullValueHandling.Ignore)]
        public int? ScreenResolutionHeight { set; get; }

        [JsonProperty(PropertyName = Protocol.TRACKER_SCREEN_PHYSICAL_WIDTH, NullValueHandling = NullValueHandling.Ignore)]
        public float? ScreenPhysicalWidth { set; get; }

        [JsonProperty(PropertyName = Protocol.TRACKER_SCREEN_PHYSICAL_HEIGHT, NullValueHandling = NullValueHandling.Ignore)]
        public float? ScreenPhysicalHeight { set; get; }
    }
}
