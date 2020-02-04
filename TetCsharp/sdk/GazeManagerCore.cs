﻿/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EyeTribe.ClientSdk.Data;
using EyeTribe.ClientSdk.Response;
using EyeTribe.ClientSdk.Request;

namespace EyeTribe.ClientSdk
{
    /// <summary>
    /// This is the core implementation of EyeTribe C# SDK. This class manages all underlying routines
    /// associated with communicating with a running EyeTribe Server.
    /// </summary>
    abstract public class GazeManagerCore : IGazeApiReponseListener, IGazeApiConnectionListener
    {
        #region Constants

        public const long DEFAULT_TIMEOUT_SECONDS = 10;
        public const long DEFAULT_TIMEOUT_MILLIS = DEFAULT_TIMEOUT_SECONDS * 1000;

        #endregion

        #region Enums

        /// <summary>
        /// The possible states of the Tracer device
        /// </summary>
        public enum TrackerState
        {
            ///<summary>Tracker device is detected and working</summary>
            TRACKER_CONNECTED = 0,
            ///<summary>Tracker device is not detected</summary>
            TRACKER_NOT_CONNECTED = 1,
            ///<summary>Tracker device is detected but not working due to wrong/unsupported firmware</summary>
            TRACKER_CONNECTED_BADFW = 2,
            ///<summary>Tracker device is detected but not working due to unsupported USB host</summary>
            TRACKER_CONNECTED_NOUSB3 = 3,
            ///<summary>Tracker device is detected but not working due to no stream could be received</summary>
            TRACKER_CONNECTED_NOSTREAM = 4,
            ///<summary>Tracker state is undefined. GazeManager not activated</summary>
            TRACKER_UNDEFINED = -1,
        }

        /// <summary>
        /// Mode in witch the EyeTribe server delivers gaze data stream
        /// </summary>
        public enum ClientMode
        {
            [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
            Push = 1001,
            [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
            Pull = 1002
        }

        /// <summary>
        /// The possible frame rates of the EyeTribe Server
        /// </summary>
        public enum FrameRate
        {
            ///<summary>FrameRate is undefined. GazeManager not activated</summary>
            FPS_UNDEFINED = 0,
            FPS_30 = 30,
            FPS_60 = 60,
        }

        /// <summary>
        /// The EyeTribe API compliance levels
        /// </summary>
        public enum ApiVersion
        {
            ///<summary>ApiVersion is undefined. GazeManager not activated</summary>
            VERSION_UNDEFINED = 0,
            VERSION_1_0 = 1
        }

        private void ResetEnums()
        {
            Trackerstate = TrackerState.TRACKER_UNDEFINED;
            Framerate = FrameRate.FPS_UNDEFINED;
            version = ApiVersion.VERSION_UNDEFINED;
        }

        #endregion

        #region Variables

        internal GazeApiManager ApiManager;

        protected bool IsActive;

        protected static readonly Object InitializationLock = new Object();

        protected int SampledCalibrationPoints;
        protected int TotalCalibrationPoints;

        internal SynchronizedCollection<IGazeListener> GazeListeners;
        internal SynchronizedCollection<ICalibrationResultListener> CalibrationResultListeners;
        internal SynchronizedCollection<ITrackerStateListener> TrackerStateListeners;
        internal SynchronizedCollection<IScreenStateListener> ScreenStateListeners;
        internal SynchronizedCollection<IConnectionStateListener> ConnectionStateListeners;
        internal SynchronizedCollection<ICalibrationStateListener> CalibrationStateListeners;
        protected ICalibrationProcessHandler _CalibrationProcessListener;

        protected GazeData LatestGazeData;

        private static bool _IsDebug;

        #endregion

        #region Constructor

        protected GazeManagerCore()
        {
            GazeListeners = new SynchronizedCollection<IGazeListener>();
            CalibrationResultListeners = new SynchronizedCollection<ICalibrationResultListener>();
            TrackerStateListeners = new SynchronizedCollection<ITrackerStateListener>();
            ScreenStateListeners = new SynchronizedCollection<IScreenStateListener>();
            ConnectionStateListeners = new SynchronizedCollection<IConnectionStateListener>();
            CalibrationStateListeners = new SynchronizedCollection<ICalibrationStateListener>();
            ResetEnums();
        }

        #endregion

        #region Get/Set

        /// <summary>
        /// Is the client library connected to EyeTribe Server?
        /// </summary>
        [Obsolete("Deprecated, use IsActivated() instead", false)]
        public bool IsConnected
        {
            get { return IsActive; }
        }

        /// <summary>
        /// Is the client library connected to EyeTribe Server and initialized?
        /// </summary>
        public bool IsActivated
        {
            get { return (null != ApiManager ? ApiManager.IsConnected() : false) && IsActive; }
        }

        /// <summary>
        /// The current state of the connected TrackerDevice.
        /// </summary>
        public TrackerState Trackerstate
        {
            get;
            private set;
        }

        /// <summary>
        /// The lastest performed and valid CalibrationResult. Note the result is not nessesarily positive
        /// and clients should evaluate the result before using. 
        /// </summary>
        public CalibrationResult LastCalibrationResult
        {
            get;
            private set;
        }

        /// <summary>
        /// Is the client in the middle of a calibration process?
        /// </summary>
        public Boolean IsCalibrating
        {
            get;
            private set;
        }

        /// <summary>
        /// Is the client already calibrated?
        /// </summary>
        public Boolean IsCalibrated
        {
            get;
            private set;
        }

        /// <summary>
        /// Index of currently used screen. Used for multiscreen setups.
        /// </summary>
        public int ScreenIndex
        {
            get;
            private set;
        }

        /// <summary>
        /// Width of screen resolution in pixels
        /// </summary>
        public int ScreenResolutionWidth
        {
            get;
            private set;
        }

        /// <summary>
        /// Height of screen resolution in pixels
        /// </summary>
        public int ScreenResolutionHeight
        {
            get;
            private set;
        }

        /// <summary>
        /// Physical width of screen in meters
        /// </summary>
        public float ScreenPhysicalWidth
        {
            get;
            private set;
        }

        /// <summary>
        /// Physical height of screen in meters
        /// </summary>
        public float ScreenPhysicalHeight
        {
            get;
            private set;
        }

        /// <summary>
        /// Length of a heartbeat in milliseconds. 
        /// The EyeTribe Server defines the desired length of a heartbeat and is in
        /// this implementation automatically acquired through the Tracker API.
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 using Heartbeat no longer has any effect", false)]
        internal int HeartbeatMillis
        {
            get;
            private set;
        }

        /// <summary>
        /// Number of frames per second delivered by EyeTribe Server
        /// </summary>
        public FrameRate Framerate
        {
            get;
            private set;
        }

        /// <summary>
        /// Current API version compliance of EyeTribe Server
        /// </summary>
        protected ApiVersion version
        {
            get;
            private set;
        }

        /// <summary>
        /// Current running mode of this client
        /// </summary>
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
        protected ClientMode clientMode
        {
            get;
            private set;
        }

        /// <summary>
        /// Is the client runnning in debug mode and thereby outputting debug info?
        /// </summary>
        public static bool DebugMode
        {
            get { return _IsDebug; }
            set { _IsDebug = value; }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Internal callback method. Should not be called directly.
        /// </summary>
        public void OnGazeApiResponse(ResponseBase response, IRequest request)
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleApiResponse), new Object[] { response, request });
        }

        internal void HandleApiResponse(Object stateInfo)
        {
            try
            {
                Object[] objs = (Object[])stateInfo;
                ResponseBase response = (ResponseBase)objs[0];
                IRequest request = (IRequest)objs[1];

                // Optional parsing phase for implementing classes
                if (ParseApiResponse(stateInfo))
                {
                    //Optional parsing successful, consume reponse
                }
                else
                {
                    switch (response.Category)
                    {
                        case Protocol.CATEGORY_TRACKER:

                            if (response.Request.Equals(Protocol.TRACKER_REQUEST_GET))
                            {
                                TrackerGetResponse tgr = (TrackerGetResponse)response;

                                if (null != tgr.Values.Version)
                                    version = (ApiVersion)tgr.Values.Version;

                                if (null != tgr.Values.FrameRate)
                                    Framerate = (FrameRate)tgr.Values.FrameRate;

                                if (null != tgr.Values.TrackerState)
                                {
                                    //if tracker state changed, notify listeners
                                    if ((int)tgr.Values.TrackerState != (int)Convert.ChangeType(Trackerstate, Trackerstate.GetTypeCode()))
                                    {
                                        Trackerstate = (TrackerState)tgr.Values.TrackerState;

                                        foreach (ITrackerStateListener listener in TrackerStateListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnTrackerStateChanged), new Object[] { listener, Trackerstate });
                                        }
                                    }
                                }

                                bool calibStateChanged = false;
                                if (null != tgr.Values.IsCalibrating)
                                {
                                    if (IsCalibrating != (bool)tgr.Values.IsCalibrating)
                                        calibStateChanged = true;
                                    IsCalibrating = (bool)tgr.Values.IsCalibrating;
                                }
                                if (null != tgr.Values.IsCalibrated)
                                {
                                    if (IsCalibrated != (bool)tgr.Values.IsCalibrated)
                                        calibStateChanged = true;
                                    IsCalibrated = (bool)tgr.Values.IsCalibrated;
                                }

                                // If calibration state changed, notify listeners
                                if (calibStateChanged)
                                    foreach (ICalibrationStateListener listener in CalibrationStateListeners)
                                    {
                                        ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationStateChanged), new Object[] { listener, IsCalibrating, IsCalibrated });
                                    }

                                if (null != tgr.Values.CalibrationResult)
                                {
                                    //is result different from current?
                                    if (null == LastCalibrationResult || !LastCalibrationResult.Equals(tgr.Values.CalibrationResult))
                                    {
                                        LastCalibrationResult = tgr.Values.CalibrationResult;

                                        foreach (ICalibrationResultListener listener in CalibrationResultListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationChanged), new Object[] { listener, IsCalibrated, LastCalibrationResult });
                                        }
                                    }
                                }

                                if (null != tgr.Values.ScreenResolutionWidth)
                                    ScreenResolutionWidth = (int)tgr.Values.ScreenResolutionWidth;

                                if (null != tgr.Values.ScreenResolutionHeight)
                                    ScreenResolutionHeight = (int)tgr.Values.ScreenResolutionHeight;

                                if (null != tgr.Values.ScreenPhysicalWidth)
                                    ScreenPhysicalWidth = (float)tgr.Values.ScreenPhysicalWidth;

                                if (null != tgr.Values.ScreenPhysicalHeight)
                                    ScreenPhysicalHeight = (float)tgr.Values.ScreenPhysicalHeight;

                                if (null != tgr.Values.ScreenIndex)
                                {
                                    //if screen index changed, notify listeners
                                    if ((int)tgr.Values.ScreenIndex != ScreenIndex)
                                    {
                                        ScreenIndex = (int)tgr.Values.ScreenIndex;

                                        foreach (IScreenStateListener listener in ScreenStateListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnScreenStatesChanged), new Object[] { listener, ScreenIndex, ScreenResolutionWidth, ScreenResolutionHeight, ScreenPhysicalWidth, ScreenPhysicalHeight });
                                        }
                                    }
                                }

                                if (null != tgr.Values.Frame)
                                {
                                    GazeData gd = tgr.Values.Frame;

                                    //fixing timestamp based on string representation, Json 32bit int issue
                                    if (!String.IsNullOrEmpty(gd.TimeStampString))
                                    {
                                        try
                                        {
                                            DateTime gdTime = DateTime.ParseExact(gd.TimeStampString, GazeData.TIMESTAMP_STRING_FORMAT,
                                                System.Globalization.CultureInfo.InvariantCulture);
                                            gd.TimeStamp = (long)((double)gdTime.Ticks / TimeSpan.TicksPerMillisecond);
                                        }
                                        catch (Exception e)
                                        {
                                            //consume possible error
                                        }
                                    }

                                    LatestGazeData = gd;

                                    foreach (IGazeListener listener in GazeListeners)
                                    {
                                        ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnGazeFrame), new Object[] { listener, gd });
                                    }
                                }
                            }
                            else if (response.Request.Equals(Protocol.TRACKER_REQUEST_SET))
                            {
                                //Do nothing
                            }
                            break;

                        case Protocol.CATEGORY_CALIBRATION:

                            switch (response.Request)
                            {
                                case Protocol.CALIBRATION_REQUEST_START:

                                    if (!IsCalibrating)
                                    {
                                        IsCalibrating = true;

                                        // Calibration state changed, notify state listeners
                                        foreach (ICalibrationStateListener listener in CalibrationStateListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationStateChanged), new Object[] { listener, IsCalibrating, IsCalibrated });
                                        }
                                    }

                                    //Notify calibration process listener that a new process was successfully started
                                    if (null != _CalibrationProcessListener)
                                        try
                                        {
                                            _CalibrationProcessListener.OnCalibrationStarted();
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine("Exception while calling ICalibrationProcessHandler.OnCalibrationStarted() on listener " + _CalibrationProcessListener + ": " + e.StackTrace);
                                        }

                                    break;

                                case Protocol.CALIBRATION_REQUEST_POINTSTART:
                                    break;

                                case Protocol.CALIBRATION_REQUEST_POINTEND:

                                    CalibrationPointEndResponse cper = (CalibrationPointEndResponse)response;

                                    if (cper == null || cper.Values.CalibrationResult == null)
                                    {
                                        ++SampledCalibrationPoints;

                                        if (null != _CalibrationProcessListener)
                                        {
                                            //Notify calibration listener that a new calibration point has been sampled
                                            try
                                            {
                                                _CalibrationProcessListener.OnCalibrationProgress(SampledCalibrationPoints / TotalCalibrationPoints);
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine("Exception while calling ICalibrationProcessHandler.OnCalibrationProgress() on listener " + _CalibrationProcessListener + ": " + e.StackTrace);
                                            }

                                            if (SampledCalibrationPoints == TotalCalibrationPoints)
                                                //Notify calibration listener that all calibration points have been sampled and the analysis of the calirbation results has begun 
                                                try
                                                {
                                                    _CalibrationProcessListener.OnCalibrationProcessing();
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine("Exception while calling ICalibrationProcessHandler.OnCalibrationProcessing() on listener " + _CalibrationProcessListener + ": " + e.StackTrace);
                                                }
                                        }
                                    }
                                    else
                                    {
                                        IsCalibrated = cper.Values.CalibrationResult.Result;
                                        IsCalibrating = !cper.Values.CalibrationResult.Result;

                                        // Evaluate resample points, we decrement according to number of points needing resampling
                                        SampledCalibrationPoints -= cper.Values.CalibrationResult.Calibpoints.Where(cp => cp.State == CalibrationPoint.STATE_RESAMPLE
                                            || cp.State == CalibrationPoint.STATE_NO_DATA).Count();

                                        // Notify calibration result listeners if calibration changed
                                        if (null == LastCalibrationResult || !LastCalibrationResult.Equals(cper.Values.CalibrationResult))
                                        {
                                            LastCalibrationResult = cper.Values.CalibrationResult;

                                            foreach (ICalibrationResultListener listener in CalibrationResultListeners)
                                            {
                                                ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationChanged), new Object[] { listener, IsCalibrated, LastCalibrationResult });
                                            }
                                        }

                                        if (null != _CalibrationProcessListener)
                                        {
                                            // Notify calibration listener that calibration results are ready for evaluation
                                            try
                                            {
                                                _CalibrationProcessListener.OnCalibrationResult(cper.Values.CalibrationResult);
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine("Exception while calling ICalibrationProcessHandler.OnCalibrationResult() on listener " + _CalibrationProcessListener + ": " + e.StackTrace);
                                            }
                                        }
                                    }

                                    break;

                                case Protocol.CALIBRATION_REQUEST_ABORT:

                                    if (IsCalibrating)
                                    {
                                        IsCalibrating = false;

                                        // Calibration state changed, notify state listeners
                                        foreach (ICalibrationStateListener listener in CalibrationStateListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationStateChanged), new Object[] { listener, IsCalibrating, IsCalibrated });
                                        }
                                    }

                                    break;

                                case Protocol.CALIBRATION_REQUEST_CLEAR:

                                    if (IsCalibrated || IsCalibrating)
                                    {
                                        IsCalibrated = false;
                                        IsCalibrating = false;

                                        // Calibration state changed, notify state listeners
                                        foreach (ICalibrationStateListener listener in CalibrationStateListeners)
                                        {
                                            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnCalibrationStateChanged), new Object[] { listener, IsCalibrating, IsCalibrated });
                                        }
                                    }

                                    LastCalibrationResult = null;
                                    break;
                            }
                            break; // end calibration switch

                        case "":

                            ResponseFailed rf = (ResponseFailed)response;

                            /* 
                             * JSON Message status code is different from HttpStatusCode.OK. Check if special TET 
                             * specific statuscode before handling error 
                             */
                            switch (rf.StatusCode)
                            {
                                case Protocol.STATUSCODE_CALIBRATION_UPDATE:
                                    //The calibration state has changed, clients should update themselves
                                    ApiManager.RequestCalibrationStates();
                                    break;

                                case Protocol.STATUSCODE_SCREEN_UPDATE:
                                    //The primary screen index has changed, clients should update themselves
                                    ApiManager.RequestScreenStates();
                                    break;

                                case Protocol.STATUSCODE_TRACKER_UPDATE:
                                    //The connected Tracker Device has changed state, clients should update themselves
                                    ApiManager.RequestTrackerState();
                                    break;

                                default:
                                    Debug.WriteLine("Request FAILED");
                                    Debug.WriteLine("Category: " + rf.Category);
                                    Debug.WriteLine("Request: " + rf.Request);
                                    Debug.WriteLine("StatusCode: " + rf.StatusCode);
                                    Debug.WriteLine("StatusMessage: " + rf.Values.StatusMessage);
                                    break;
                            }

                            break;
                    }
                }

                if (null != request)
                    request.Finish();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception while parsing API response: " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnGazeFrame(Object stateInfo)
        {
            IGazeListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (IGazeListener)objs[0];
                GazeData gd = (GazeData)objs[1];
                listener.OnGazeUpdate(gd);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling IGazeListener.OnGazeUpdate() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnTrackerStateChanged(Object stateInfo)
        {
            ITrackerStateListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (ITrackerStateListener)objs[0];
                TrackerState state = (TrackerState)objs[1];
                listener.OnTrackerStateChanged(state);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling ITrackerStateListener.OnTrackerStateChanged() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnCalibrationChanged(Object stateInfo)
        {
            ICalibrationResultListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (ICalibrationResultListener)objs[0];
                Boolean isCalibrated = Convert.ToBoolean(objs[1]);
                CalibrationResult lastResult = (CalibrationResult)objs[2];
                listener.OnCalibrationChanged(isCalibrated, lastResult);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling ICalibrationResultListener.OnCalibrationChanged() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnScreenStatesChanged(Object stateInfo)
        {
            IScreenStateListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (IScreenStateListener)objs[0];
                Int32 screenIndex = Convert.ToInt32(objs[1]);
                Int32 screenResolutionWidth = Convert.ToInt32(objs[2]);
                Int32 screenResolutionHeight = Convert.ToInt32(objs[3]);
                Double screenPhysicalWidth = Convert.ToDouble(objs[4]);
                Double screenPhysicalHeight = Convert.ToDouble(objs[5]);
                listener.OnScreenStatesChanged(screenIndex, screenResolutionWidth, screenResolutionHeight, (float)screenPhysicalWidth, (float)screenPhysicalHeight);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling ITrackerStateListener.OnScreenStatesChanged() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal callback method. Should not be called directly.
        /// </summary>
        public void OnGazeApiConnectionStateChanged(bool isConnected)
        {
            foreach (IConnectionStateListener listener in ConnectionStateListeners)
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(HandleOnConnectionStateChanged), new Object[] { listener, isConnected });
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnConnectionStateChanged(Object stateInfo)
        {
            IConnectionStateListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (IConnectionStateListener)objs[0];
                Boolean isConnected = (Boolean)objs[1];
                listener.OnConnectionStateChanged(isConnected);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling IConnectionStateListener.OnConnectionStateChanged() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Internal delegate helper method. Used for ThreadPooling.
        /// </summary>
        internal static void HandleOnCalibrationStateChanged(Object stateInfo)
        {
            ICalibrationStateListener listener = null;
            try
            {
                Object[] objs = (Object[])stateInfo;
                listener = (ICalibrationStateListener)objs[0];
                Boolean isCalibrating = (Boolean)objs[1];
                Boolean isCalibrated = (Boolean)objs[2];
                listener.OnCalibrationStateChanged(isCalibrating, isCalibrated);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while calling ICalibrationStateListener.OnCalibrationStateChanged() on listener " + listener + ": " + e.StackTrace);
            }
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines using default values. Latest API version will be used in
        /// client mode PUSH. This call is synchronous and calling thread is locked during initialization.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <returns>True if succesfully activated, false otherwise</returns>
        public bool Activate()
        {
            return Activate(ApiVersion.VERSION_1_0, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is synchronous and calling thread is locked
        /// during initialization.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        public bool Activate(ApiVersion apiVersion)
        {
            return Activate(apiVersion, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT);
        }
        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines using default values. Should be called _only_ 
        /// once when an application starts up. Calling thread will be locked during
        /// initialization.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="mode">Mode though which the client will receive GazeData. Either ClientMode.Push or ClientMode.Pull</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
        public bool Activate(ApiVersion apiVersion, ClientMode mode)
        {
            //Connect using default 'Push' mode
            return Activate(apiVersion, mode, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT, null);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. Should be called _only_ 
        /// once when an application starts up. Calling thread will be locked during
        /// initialization.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="mode">Mode though which the client will receive GazeData. Either ClientMode.Push or ClientMode.Pull</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
        public bool Activate(ApiVersion apiVersion, ClientMode mode, string hostname, int portnumber)
        {
            //Connect using default 'Push' mode
            return Activate(apiVersion, mode, hostname, portnumber, null);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines using default values. Should be called _only_ 
        /// once when an application starts up. Calling thread will be locked during
        /// initialization.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="mode">Mode though which the client will receive GazeData. Either ClientMode.Push or ClientMode.Pull</param>
        /// <param name="listener">Listener to notify once the connection to EyeTribe Server has been established</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
        public bool Activate(ApiVersion apiVersion, ClientMode mode, IConnectionStateListener listener)
        {
            //Connect using default 'Push' mode
            return Activate(apiVersion, mode, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT, listener);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. Should be called _only_ 
        /// once when an application starts up. Calling thread will be locked during
        /// initialization.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="mode">Mode though which the client will receive GazeData. Either ClientMode.Push or ClientMode.Pull</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <param name="listener">Listener to notify once the connection to EyeTribe Server has been established</param> 
        /// <returns>True if succesfully activated, false otherwise</returns>
        [Obsolete("Deprecated, as of EyeTribe Server v.0.9.77 ClientMode Push is default", false)]
        public bool Activate(ApiVersion apiVersion, ClientMode mode, string hostname, int portnumber, IConnectionStateListener listener)
        {
            AddConnectionStateListener(listener);
            //Connect using default 'Push' mode
            return Activate(apiVersion, hostname, portnumber);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is synchronous and calling thread is locked
        /// during initialization.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        public bool Activate(ApiVersion apiVersion, string hostname, int portnumber)
        {
            return Activate(apiVersion, hostname, portnumber, DEFAULT_TIMEOUT_MILLIS);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is synchronous and calling thread is locked
        /// during initialization.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <param name="timeout">time out in milliseconds of connection attempt</param>
        /// <returns>True if succesfully activated, false otherwise</returns>
        public bool Activate(ApiVersion apiVersion, string hostname, int portnumber, long timeout)
        {
            return ActivateAsync(apiVersion, hostname, portnumber, timeout).Result;
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines using default values. Latest API version will be
        /// used in client mode PUSH. This call is asynchronous.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync()
        {
            return ActivateAsync(ApiVersion.VERSION_1_0, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines using default values. Latest API version will be
        /// used in client mode PUSH. This call is asynchronous.
        /// <para/>
        /// During the set timeout, an amount of connection retries will be attempted.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="timeout">time out in milliseconds of connection attempt</param>
        /// <param name="retries">number of times to retry connection attempt in the timeout period</param>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync(long timeout, int retries)
        {
            return ActivateAsync(ApiVersion.VERSION_1_0, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT, timeout, retries);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is asynchronous.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync(ApiVersion apiVersion)
        {
            return ActivateAsync(apiVersion, GazeApiManager.DEFAULT_SERVER_HOST, GazeApiManager.DEFAULT_SERVER_PORT);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is asynchronous.
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync(ApiVersion apiVersion, string hostname, int portnumber)
        {
            return ActivateAsync(apiVersion, hostname, portnumber, DEFAULT_TIMEOUT_MILLIS, 1);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is asynchronous.
        /// <para/>
        /// During the set timeout, an amount of connection retries will be attempted. Time frame of an activation attempt
        /// should be around 3 seconds e.g. 3 retries in 10 seconds
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <param name="timeout">timeout in milliseconds of connection attempt</param>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync(ApiVersion apiVersion, string hostname, int portnumber, long timeout)
        {
            return ActivateAsync(apiVersion, hostname, portnumber, timeout, 1);
        }

        /// <summary>
        /// Activates EyeTribe C# SDK and all underlying routines. This call is asynchronous.
        /// <para/>
        /// During the set timeout, an amount of connection retries will be attempted. Time frame of an activation attempt
        /// should be around 3 seconds e.g. 3 retries in 10 seconds
        /// <para/>
        /// To shutdown, the <see cref="GazeManager.Deactivate()"/> method must be called.
        /// </summary>
        /// <param name="apiVersion">Version number of the Tracker API that this client will be compliant to</param>
        /// <param name="hostname">The host name or IP address where the eye tracking server is running.</param>
        /// <param name="portnumber">The port number used for the eye tracking server</param>
        /// <param name="timeout">timeout in milliseconds of connection attempt</param>
        /// <param name="retries">number of times to retry connection attempt in the timeout period</param>
        /// <returns>a Task representing the pending activation attempt</returns>
        public Task<Boolean> ActivateAsync(ApiVersion apiVersion, string hostname, int portnumber, long timeout, int retries)
        {
            return Task.Factory.StartNew<Boolean>(() =>
            {
                //only one entity can initialize at the time
                lock (InitializationLock)
                {
                    if (!IsActivated)
                    {
                        int retryDelay = (int)Math.Round((float)timeout / retries);

                        if (_IsDebug)
                            Debug.WriteLine("retryDelay: " + retryDelay);

                        try
                        {
                            int numRetries = 0;

                            while (numRetries++ < retries)
                            {
                                long timstampStart = (long)((double)DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);

                                if (Initialize(apiVersion, hostname, portnumber, retryDelay))
                                {
                                    break; // success, break loop
                                }
                                else
                                {
                                    // Short delay before retrying
                                    long timePassed = (long)((double)DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - timstampStart;

                                    if (timePassed < retryDelay)
                                    {
                                        Thread.Sleep((int)(retryDelay - timePassed));
                                    }

                                    if (_IsDebug)
                                        Debug.WriteLine("Connection Failed, num retry: " + numRetries);
                                }
                            }
                        }
                        catch (ThreadInterruptedException tie)
                        {
                            Debug.WriteLine("EyeTribe Server connection attempt interrupted: " + tie.Message);
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("Exception while establishing EyeTribe Server connection: " + e.Message);
                        }
                    }
                }

                return IsActivated;
            });
        }

        protected virtual Boolean Initialize(ApiVersion apiVersion, string hostname, int portnumber, long timeOut)
        {
            try
            {
                // initialize networking
                if (null == ApiManager)
                    ApiManager = CreateApiManager(this, this);
                else
                    ApiManager.Close();

                var IsInitialized = false;

                if (ApiManager.Connect(hostname, portnumber, timeOut))
                {
                    ApiManager.RequestTracker(apiVersion);
                    var asyncLock = ApiManager.RequestAllStates();

                    // We await gaze manager state initialization before proceeding
                    lock (asyncLock)
                    {
                        Monitor.Wait(asyncLock, (int)timeOut);

                        IsInitialized = (bool)asyncLock[0];
                    }

                    if (IsInitialized)
                    {
                        IsActive = true;

                        //notify connection listeners
                        OnGazeApiConnectionStateChanged(IsActivated);
                    }
                }

                if (!IsInitialized)
                {
                    HandleInitFailure();

                    Console.WriteLine("Error initializing GazeManager, is EyeTribe Server running?");
                }
            }
            catch (Exception e)
            {
                HandleInitFailure();

                Console.WriteLine("Error initializing GazeManager: " + e.Message + e.StackTrace);
            }

            return IsActivated;
        }

        internal void HandleInitFailure()
        {
            if (null != ApiManager)
                ApiManager.Close();

            IsActive = false;
        }

        /// <summary>
        /// Deactivates EyeTribe C# SDK and all under lying routines. Should be called when
        /// a application closes down.
        /// </summary>
        public void Deactivate()
        {
            //lock to ensure that state changing method calls are synchronous
            lock (InitializationLock)
            {
                ClearListeners();

                if (null != ApiManager)
                    ApiManager.Close();

                ResetEnums();

                IsActive = false;
            }
        }

        /// <summary>
        /// Adds a <see cref="EyeTribe.IGazeListener"/> to the EyeTribe C# SDK. This listener 
        /// will recieve <see cref="EyeTribe.Data.GazeData"/> updates when available
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.IGazeListener"/> instance to add</param>
        public void AddGazeListener(IGazeListener listener)
        {
            AddListener<IGazeListener>(GazeListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.IGazeListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.IGazeListener"/> instance to remove</param>
        public bool RemoveGazeListener(IGazeListener listener)
        {
            return RemoveListener<IGazeListener>(GazeListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.IGazeListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumGazeListeners()
        {
            return GazeListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.IGazeListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasGazeListener(IGazeListener listener)
        {
            return HasListener<IGazeListener>(GazeListeners, listener);
        }

        /// <summary>
        /// Adds a <see cref="ICalibrationResultListener"/> to the EyeTribe C# SDK. This listener 
        /// will recieve updates about calibration state changes.
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationResultListener"/> instance to add</param>
        public void AddCalibrationResultListener(ICalibrationResultListener listener)
        {
            AddListener<ICalibrationResultListener>(CalibrationResultListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.ICalibrationResultListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationResultListener"/> instance to remove</param>
        public bool RemoveCalibrationResultListener(ICalibrationResultListener listener)
        {
            return RemoveListener<ICalibrationResultListener>(CalibrationResultListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.ICalibrationResultListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumCalibrationResultListeners()
        {
            return CalibrationResultListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.ICalibrationResultListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasCalibrationResultListener(ICalibrationResultListener listener)
        {
            return HasListener<ICalibrationResultListener>(CalibrationResultListeners, listener);
        }

        /// <summary>
        /// Adds a <see cref="EyeTribe.ITrackerStateListener"/> to the EyeTribe C# SDK. This listener 
        /// will recieve updates about change of active screen index.
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.ITrackerStateListener"/> instance to add</param>
        public void AddTrackerStateListener(ITrackerStateListener listener)
        {
            AddListener<ITrackerStateListener>(TrackerStateListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.ITrackerStateListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.ITrackerStateListener"/> instance to remove</param>
        public bool RemoveTrackerStateListener(ITrackerStateListener listener)
        {
            return RemoveListener<ITrackerStateListener>(TrackerStateListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.ITrackerStateListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumTrackerStateListeners()
        {
            return TrackerStateListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.ITrackerStateListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasTrackerStateListener(ITrackerStateListener listener)
        {
            return HasListener<ITrackerStateListener>(TrackerStateListeners, listener);
        }

        /// <summary>
        /// Adds a <see cref="EyeTribe.IScreenStateListener"/> to the EyeTribe C# SDK. This listener will 
        /// receive updates about change of active screen index.
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.IScreenStateListener"/> instance to add</param>
        public void AddScreenStateListener(IScreenStateListener listener)
        {
            AddListener<IScreenStateListener>(ScreenStateListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.IScreenStateListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.IScreenStateListener"/> instance to remove</param>
        public bool RemoveScreenStateListener(IScreenStateListener listener)
        {
            return RemoveListener<IScreenStateListener>(ScreenStateListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.IScreenStateListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumScreenStateListeners()
        {
            return ScreenStateListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.IScreenStateListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasScreenStateListener(IScreenStateListener listener)
        {
            return HasListener<IScreenStateListener>(ScreenStateListeners, listener);
        }

        /// <summary>
        /// Adds a <see cref="EyeTribe.IConnectionStateListener"/> to the EyeTribe C# SDK. This listener 
        /// will recieve updates about change in connection state to the EyeTribe Server.
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.IConnectionStateListener"/> instance to add</param>
        public void AddConnectionStateListener(IConnectionStateListener listener)
        {
            AddListener<IConnectionStateListener>(ConnectionStateListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.IConnectionStateListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.IConnectionStateListener"/> instance to remove</param>
        public bool RemoveConnectionStateListener(IConnectionStateListener listener)
        {
            return RemoveListener<IConnectionStateListener>(ConnectionStateListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.IConnectionStateListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumConnectionStateListeners()
        {
            return ConnectionStateListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.IConnectionStateListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasConnectionStateListener(IConnectionStateListener listener)
        {
            return HasListener<IConnectionStateListener>(ConnectionStateListeners, listener);
        }

        /// <summary>
        /// Adds a <see cref="EyeTribe.ICalibrationStateListener"/> to the EyeTribe C# SDK. This listener 
        /// will recieve updates about change in calibration state in the EyeTribe Server.
        /// </summary>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationStateListener"/> instance to add</param>
        public void AddCalibrationStateListener(ICalibrationStateListener listener)
        {
            AddListener<ICalibrationStateListener>(CalibrationStateListeners, listener);
        }

        /// <summary>
        /// Remove a <see cref="EyeTribe.ICalibrationStateListener"/> from the EyeTribe C# SDK.
        /// </summary>
        /// <returns>True if succesfully removed, false otherwise</returns>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationStateListener"/> instance to remove</param>
        public bool RemoveCalibrationStateListener(ICalibrationStateListener listener)
        {
            return RemoveListener<ICalibrationStateListener>(CalibrationStateListeners, listener);
        }

        /// <summary>
        /// Gets current number of attached <see cref="EyeTribe.ICalibrationStateListener"/> instances.
        /// </summary>
        /// <returns>Curent number of listeners</returns>
        public int GetNumCalibrationStateListeners()
        {
            return CalibrationStateListeners.Count;
        }

        /// <summary>
        /// Checkes if a given instance of <see cref="EyeTribe.ICalibrationStateListener"/> is currently attached.
        /// </summary>
        /// <returns>True if already attached, false otherwise</returns>
        public bool HasCalibrationStateListener(ICalibrationStateListener listener)
        {
            return HasListener<ICalibrationStateListener>(CalibrationStateListeners, listener);
        }

        protected void AddListener<T>(SynchronizedCollection<T> coll, T listener)
        {
            if (null != listener)
            {
                if (!coll.Contains(listener))
                    coll.Add(listener);
            }
        }

        protected bool RemoveListener<T>(SynchronizedCollection<T> coll, T listener)
        {
            bool result = false;

            if (null != listener)
            {
                if (coll.Contains(listener))
                    result = coll.Remove(listener);
            }

            return result;
        }

        protected bool HasListener<T>(SynchronizedCollection<T> coll, T listener)
        {
            bool result = false;

            if (null != listener)
            {
                result = coll.Contains(listener);
            }

            return result;
        }

        /// <summary>
        /// Clear all attached listeners, clears GazeData queue and stop broadcating
        /// </summary>
        public virtual void ClearListeners()
        {
            if (null != GazeListeners)
                GazeListeners.Clear();

            if (null != CalibrationResultListeners)
                CalibrationResultListeners.Clear();

            if (null != TrackerStateListeners)
                TrackerStateListeners.Clear();

            if (null != ScreenStateListeners)
                ScreenStateListeners.Clear();

            if (null != ConnectionStateListeners)
                ConnectionStateListeners.Clear();

            if (null != CalibrationStateListeners)
                CalibrationStateListeners.Clear();
        }

        /// <summary>
        /// Switch currently active screen. Enabled the user to take control of which screen is used for calibration 
        /// and gaze control.
        /// </summary>
        /// <para/>
        /// This call is synchronous and calling thread is locked while request is processed.
        /// 
        /// <param name="screenIndex">Index of nex screen. On windows 'Primary Screen' has index 0.</param>
        /// <param name="screenResW">Screen resolution width in pixels</param>
        /// <param name="screenResH">Screen resolution height in pixels</param>
        /// <param name="screenPsyW">Physical Screen width in meters</param>
        /// <param name="screenPsyH">Physical Screen height in meters</param>
        /// <returns>True if request successful, false otherwise</returns>
        public Boolean SwitchScreen(int screenIndex, int screenResW, int screenResH, float screenPsyW, float screenPsyH)
        {
            return SwitchScreenAsync(screenIndex, screenResW, screenResH, screenPsyW, screenPsyH).Result;
        }

        /// <summary>
        /// Switch currently active screen. Enabled the user to take control of which screen is used for calibration 
        /// and gaze control.
        /// </summary>
        /// <para/>
        /// This call is asynchronous.
        /// 
        /// <param name="screenIndex">Index of nex screen. On windows 'Primary Screen' has index 0.</param>
        /// <param name="screenResW">Screen resolution width in pixels</param>
        /// <param name="screenResH">Screen resolution height in pixels</param>
        /// <param name="screenPsyW">Physical Screen width in meters</param>
        /// <param name="screenPsyH">Physical Screen height in meters</param>
        /// <returns>a Task representing the pending screen switch attempt</returns>
        public Task<Boolean> SwitchScreenAsync(int screenIndex, int screenResW, int screenResH, float screenPsyW, float screenPsyH)
        {
            return Task.Factory.StartNew<Boolean>(() =>
            {
                if (IsActivated)
                {
                    Object[] asyncLock = ApiManager.RequestScreenSwitch(screenIndex, screenResW, screenResH, screenPsyW, screenPsyH);

                    lock (asyncLock)
                    {
                        try
                        {
                            Monitor.Wait(asyncLock, (int)DEFAULT_TIMEOUT_MILLIS);
                        }
                        catch (ThreadInterruptedException tie)
                        {
                            //consume
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception while awaiting reply to sync request: " + e.Message);
                            //e.printStackTrace();
                        }
                    }

                    return this.ScreenIndex == screenIndex &&
                        this.ScreenResolutionWidth == screenResW &&
                        this.ScreenResolutionHeight == screenResH &&
                        this.ScreenPhysicalWidth == screenPsyW &&
                        this.ScreenPhysicalHeight == screenPsyH;
                }

                Console.WriteLine("EyeTribe C# SDK not activated!");

                return false;
            });
        }

        /// <summary>
        /// Initiate a new calibration process. Must be called before any call to <see cref="EyeTribe.GazeManager.CalibrationPointStart(int, int)"/> 
        /// or <see cref="EyeTribe.GazeManager.CalibrationPointEnd()"/> .
        /// <para/>
        /// Any previous (and possible running) calibration process must be completed or aborted before calling this. Otherwise the server will
        /// return an error.
        /// <para/>
        /// A full calibration process consists of a number of calls to <see cref="EyeTribe.GazeManager.CalibrationPointStart(int, int)"/> 
        /// and <see cref="EyeTribe.GazeManager.CalibrationPointEnd()"/>  matching the total number of clibration points set by the
        /// numCalibrationPoints parameter.
        /// <para/>
        /// This call is synchronous and calling thread is locked while request is processed.
        /// </summary>
        /// <param name="numCalibrationPoints">The number of calibration points that will be used in this calibration</param>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationProcessHandler"/>  instance that will receive callbacks during the 
        /// calibration process</param>
        /// <returns>true if a new calibration process was successfully started, false otherwise</returns>
        public Boolean CalibrationStart(short numCalibrationPoints, ICalibrationProcessHandler listener)
        {
            return CalibrationStartAsync(numCalibrationPoints, listener).Result;
        }

        /// <summary>
        /// Initiate a new calibration process. Must be called before any call to <see cref="EyeTribe.GazeManager.CalibrationPointStart(int, int)"/> 
        /// or <see cref="EyeTribe.GazeManager.CalibrationPointEnd()"/> .
        /// <para/>
        /// Any previous (and possible running) calibration process must be completed or aborted before calling this. Otherwise the server will
        /// return an error.
        /// <para/>
        /// A full calibration process consists of a number of calls to <see cref="EyeTribe.GazeManager.CalibrationPointStart(int, int)"/> 
        /// and <see cref="EyeTribe.GazeManager.CalibrationPointEnd()"/>  matching the total number of clibration points set by the
        /// numCalibrationPoints parameter.
        /// <para/>
        /// This call is asynchronous.
        /// </summary>
        /// <param name="numCalibrationPoints">The number of calibration points that will be used in this calibration</param>
        /// <param name="listener">The <see cref="EyeTribe.ICalibrationProcessHandler"/>  instance that will receive callbacks during the 
        /// calibration process</param>
        /// <returns>a Task representing the pending calibration start attempt</returns>
        public Task<Boolean> CalibrationStartAsync(short numCalibrationPoints, ICalibrationProcessHandler listener)
        {
            return Task.Factory.StartNew<Boolean>(() =>
            {
                if (IsActivated)
                {
                    if (!IsCalibrating)
                    {
                        SampledCalibrationPoints = 0;
                        TotalCalibrationPoints = numCalibrationPoints;
                        _CalibrationProcessListener = listener;

                        Object[] asyncLock = ApiManager.RequestCalibrationStart(numCalibrationPoints);

                        lock (asyncLock)
                        {
                            try
                            {
                                Monitor.Wait(asyncLock, (int)DEFAULT_TIMEOUT_MILLIS);
                            }
                            catch (ThreadInterruptedException tie)
                            {
                                //consume
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Exception while awaiting reply to sync request: " + e.Message);
                            }
                        }

                        return IsCalibrating;
                    }

                    Console.WriteLine("Calibration process already started! Abort ongoing calibration to start new.");

                    return false;
                }

                Console.WriteLine("EyeTribe C# SDK not activated!");

                return false;
            });
        }

        /// <summary>
        /// Called for every calibration point during a calibration process. This call should be followed by a call to
        /// <see cref="EyeTribe.GazeManager.CalibrationPointEnd()"/>  1-2 seconds later.
        /// <para/>
        /// The calibration process must be initiated by a call to <see cref="EyeTribe.GazeManager.CalibrationStart(short, ICalibrationProcessHandler)"/>  
        /// before calling this.
        /// </summary>
        /// <param name="x">X coordinate of the calibration point</param>
        /// <param name="y">Y coordinate of the calibration point</param>
        public void CalibrationPointStart(int x, int y)
        {
            if (IsActivated)
            {
                if (IsCalibrating)
                    ApiManager.RequestCalibrationPointStart(x, y);
                else
                    Console.WriteLine("Calling CalibrationPointStart(), but EyeTribe C# SDK calibration not started!");
            }
            else
                Console.WriteLine("Calling CalibrationPointStart(), but EyeTribe C# SDK not activated!");
        }

        /// <summary>
        /// Called for every calibration point during a calibration process. This should be
        /// called 1-2 seconds after <see cref="EyeTribe.GazeManager.CalibrationPointStart(int,int)"/> .
        /// The calibration process must be initiated by a call to <see cref="CalibrationStart(short, ICalibrationProcessHandler)"/> 
        /// before calling this.
        /// </summary>
        public void CalibrationPointEnd()
        {
            if (IsActivated)
            {
                if (IsCalibrating)
                    ApiManager.RequestCalibrationPointEnd();
                else
                    Console.WriteLine("Calling CalibrationPointEnd(), but EyeTribe C# SDK calibration not started!");
            }
            else
                Console.WriteLine("Calling CalibrationPointEnd(), but EyeTribe C# SDK not activated!");
        }

        /// <summary>
        /// Cancels an ongoing calibration process.
        /// <para/>
        /// This call is synchronous and calling thread is locked while request is processed.
        /// </summary> 
        /// <returns>True is request successful, false otherwise</returns>
        public Boolean CalibrationAbort()
        {
            return CalibrationAbortAsync().Result;
        }

        /// <summary>
        /// Cancels an ongoing calibration process.
        /// <para/>
        /// This call is asynchronous.
        /// </summary> 
        /// <returns>a Task representing the pending calibration abort attempt</returns>
        public Task<Boolean> CalibrationAbortAsync()
        {
            return Task.Factory.StartNew<Boolean>(() =>
            {
                if (IsActivated)
                {
                    if (IsCalibrating)
                    {
                        Object[] asyncLock = ApiManager.RequestCalibrationAbort();

                        lock (asyncLock)
                        {
                            try
                            {
                                Monitor.Wait(asyncLock, (int)DEFAULT_TIMEOUT_MILLIS);
                            }
                            catch (ThreadInterruptedException tie)
                            {
                                //consume
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Exception while awaiting reply to sync request: " + e.Message);
                            }
                        }

                        return !IsCalibrating;
                    }

                    Console.WriteLine("Calling CalibrationAbort(), but calibration process not running.");

                    return false;
                }

                Console.WriteLine("EyeTribe C# SDK not activated!");

                return false;
            });
        }

        /// <summary>
        /// Resets calibration state, cancelling any previous calibrations.
        /// </summary>
        public void CalibrationClear()
        {
            if (IsActivated)
                ApiManager.RequestCalibrationClear();
            else
                Console.WriteLine("Calling CalibrationClear(), but EyeTribe C# SDK not activated!");
        }

        /// <summary>
        /// Classes implementing this abstract class must implement a factory method the creates the 
        /// GazeApiManager class that manages communication with the EyeTribe Server.
        /// </summary>
        internal abstract GazeApiManager CreateApiManager(IGazeApiReponseListener responseListener, IGazeApiConnectionListener connectionListener);

        /// <summary>
        /// This method allow implementing classes to add an extra parsing phase after the core parsing
        /// has been handled in <see cref="HandleApiResponse(Object)"/>.
        /// </summary>
        public virtual bool ParseApiResponse(Object stateInfo) { return false; }

        #endregion
    }

    /// <summary>
    /// Callback interface with methods associated to Gaze Tracking.
    /// This interface should be implemented by classes that are to recieve live GazeData stream.
    /// </summary>
    public interface IGazeListener
    {
        /// <summary>
        /// A notification call back indicating that a new GazeData frame is available. 
        /// Implementing classes should update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddGazeListener().
        /// </summary>
        /// <param name="gazeData">Latest GazeData frame processed by EyeTribe Server</param> 
        void OnGazeUpdate(GazeData gazeData);
    }

    /// <summary>
    /// Callback interface with methods associated to the changes in Calibration state.
    /// This interface should be implemented by classes that are to recieve notifications of 
    /// the EyeTribe Servers calibration state.
    /// </summary>
    public interface ICalibrationStateListener
    {
        /// <summary>
        /// A notification call back indicating that state of calibration has changed. 
        /// Implementing classes should update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddCalibrationStateListener().
        /// </summary>
        /// <param name="isCalibrating">is the EyeTribe Server calibrating?</param>
        /// <param name="isCalibrated">is the EyeTribe Server calibrated?</param>
        void OnCalibrationStateChanged(bool isCalibrating, bool isCalibrated);
    }

    /// <summary>
    /// Callback interface with methods associated to the changes of CalibrationResult.
    /// This interface should be implemented by classes that are to recieve only changes in CalibrationResult
    /// and who are _not_ to perform the calibration process itself.
    /// </summary>
    public interface ICalibrationResultListener
    {
        /// <summary>
        /// A notification call back indicating that state of calibration has changed. 
        /// Implementing classes should update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddCalibrationResultListener().
        /// </summary>
        /// <param name="isCalibrated">is the EyeTribe Server calibrated?</param>
        /// <param name="calibResult">if calibrated, the currently valid CalibrationResult, otherwise null</param>
        void OnCalibrationChanged(bool isCalibrated, CalibrationResult calibResult);
    }

    /// <summary>
    /// Callback interface with methods associated to the currently active calibration screen in a
    /// multi screen setup.
    /// This interface should be implemented by classes that are to receive notifications that the 
    /// main calibration screen has changed and handle these accordingly. This could be a class in 
    /// the 'View' layer telling the user that the calibration screen has changed.
    /// </summary>
    public interface IScreenStateListener
    {
        /// <summary>
        /// A notification call back indicating that main screen index has changed. 
        /// This is only relevant for multiscreen setups. Implementing classes should
        /// update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddScreenStateListener().
        /// </summary>
        /// <param name="screenIndex">the currently valid screen index</param>
        /// <param name="screenResolutionWidth">screen resolution width in pixels</param>
        /// <param name="screenResolutionHeight">screen resolution height in pixels</param>
        /// <param name="screenPhysicalWidth">Physical screen width in meters</param>
        /// <param name="screenPhysicalHeight">Physical screen height in meters</param>
        void OnScreenStatesChanged(int screenIndex, int screenResolutionWidth, int screenResolutionHeight, float screenPhysicalWidth, float screenPhysicalHeight);
    }

    /// <summary>
    /// Callback interface with methods associated to the state of the physical Tracker device.
    /// This interface should be implemented by classes that are to receive notifications of 
    /// changes in the state of the Tracker and handle these accordingly. This could be a class
    /// in the 'View' layer telling the user that a Tracker has disconnected.
    /// </summary>
    public interface ITrackerStateListener
    {
        /// <summary>
        /// A notification call back indicating that state of connected Tracker device has changed. 
        /// Use this to detect if a tracker has been connected or disconnected.
        /// Implementing classes should update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddTrackerStateListener().
        /// </summary>
        /// <param name="trackerState">the current state of the physical Tracker device</param>
        void OnTrackerStateChanged(GazeManager.TrackerState trackerState);
    }

    /// <summary>
    /// Callback interface with methods associated to Calibration process.
    /// </summary>
    public interface ICalibrationProcessHandler
    {
        /// <summary>
        /// Called when a calibration process has been started. 
        /// </summary>
        void OnCalibrationStarted();

        /// <summary>
        /// Called every time tracking of a single calibration points has completed.
        /// </summary>
        /// <param name="progress">'normalized' progress [0..1d]</param>
        void OnCalibrationProgress(double progress);

        /// <summary>
        /// Called when all calibration points have been collected and calibration processing begins.
        /// </summary>
        void OnCalibrationProcessing();

        /// <summary>
        /// Called when processing of calibration points and calibration as a whole has completed.
        /// </summary>
        /// <param name="calibResult">the results of the calibration process</param>
        void OnCalibrationResult(CalibrationResult calibResult);
    }

    /// <summary>
    /// Callback interface with methods associated to the state of the connection to the
    /// EyeTribe Server. This interface should be implemented by classes that are to
    /// receive notifications of changes in the connection state and handle these
    /// accordingly. This could be a class in the 'View' layer telling the user that the
    /// connection to the EyeTribe Server was lost.
    /// </summary>
    public interface IConnectionStateListener
    {
        /// <summary>
        /// A notification call back indicating that the connection state has changed.
        /// Use this to detect if connection the EyeTribe Server has been lost.
        /// Implementing classes should update themselves accordingly if needed.
        /// Register for updates through GazeManager.AddConnectionStateListener().
        /// </summary>
        /// <param name="isConnected">the current state of the connection</param>
        void OnConnectionStateChanged(bool isConnected);
    }
}