using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace CloudClient
{
    public sealed partial class MainPage : Page
    {
        const int bufferSize = 32;

        enum State
        {
            Outside_of_object,
            Inside_object
        }

        public async Task ReadDataFromSerialPort(string id)
        {
            var device = await Windows.Devices.SerialCommunication.SerialDevice.FromIdAsync(id);
            device.BaudRate = 115200;

            var currentStr = "";
            State state = State.Outside_of_object;
            var buffer = new Windows.Storage.Streams.Buffer(bufferSize);

            totalBytesReadFromSerial = 0;

            DataReader dataReaderObject = null;

            while (true)
            {
                try
                {
                    dataReaderObject = new DataReader(device.InputStream);

                    dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;
                    var loadAsyncTask = dataReaderObject.LoadAsync(bufferSize).AsTask();
                    uint bytesRead = await loadAsyncTask;

                    if(bytesRead > 0)
                    {
                        this.state.serialWire.Update(WireState.Solid);
                        this.state.serialWire.Update(DataFlow.Active);

                        totalBytesReadFromSerial += bytesRead;

                        var str = dataReaderObject.ReadString(bytesRead);
                        Debug.WriteLine(string.Format("[{0}]", str));
                        foreach (var c in str)
                        {
                            switch (c)
                            {
                                case '{':
                                    switch (state)
                                    {
                                        case State.Outside_of_object:
                                            currentStr = c.ToString();
                                            state = State.Inside_object;
                                            break;
                                        case State.Inside_object:
                                            // throw new NotSupportedException("Nested JSON valued are not supported");
                                            // We got into a weird state. Get out.
                                            currentStr = c.ToString();
                                            state = State.Inside_object;
                                            break;
                                    }
                                    break;
                                case '}':
                                    switch (state)
                                    {
                                        case State.Outside_of_object:
                                            // we started reading mid-stream, but now we're truly outside
                                            break;
                                        case State.Inside_object:
                                            currentStr += c;
                                            await HandleJSONObject(currentStr);
                                            // Nested are not supported
                                            state = State.Outside_of_object;
                                            currentStr = "";
                                            break;
                                    }
                                    break;
                                default:
                                    switch (state)
                                    {
                                        case State.Outside_of_object:
                                            // skip this char
                                            break;
                                        case State.Inside_object:
                                            // accumulate it:
                                            currentStr += c;
                                            break;
                                    }
                                    break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    this.state.serialWire.Update(DataFlow.Stopped);
                    this.state.serialWire.Update(WireState.Cut);
                    totalBytesReadFromSerial = 0;
                    break;
                }
                finally
                {
                    dataReaderObject.DetachStream();
                }
            }
        }

        private async Task HandleJSONObject(string json)
        {
            try
            {
                var jsonObject = JObject.Parse(json);
                try
                {
                    await SendDataToCloud(jsonObject);
                }
                catch
                {
                    this.state.cloudWire.Update(WireState.Cut);
                    this.state.cloudWire.Update(DataFlow.Stopped);
                }
            }
            catch (JsonException)
            {
                // We have bad data, ignore and get a new a new entry
            }
        }
    }
}