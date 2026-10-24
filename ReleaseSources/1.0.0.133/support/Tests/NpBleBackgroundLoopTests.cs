using System;
using System.IO;
using System.Threading.Tasks;

// The scheduling and age methods below are copied verbatim from production by
// the runner. Only their I/O/control adapters are doubled; no radio or UI opens.
sealed class NpBleBackgroundLoopTests {
    sealed class Clock {internal long ElapsedMilliseconds;}
    sealed class Retry {internal bool CanAttempt(long now){return true;}}
    sealed class Label {internal string Text="fresh";internal bool Visible;}
    static int checks;
    bool closing,bleBackgroundBusy,bleImuUnsupported,yield;
    long bleImuObservedAt=-1;
    object LastBleImu;
    readonly Clock bleImuClock=new Clock();readonly Retry bleImuRetry=new Retry();readonly Label bleImuLabel=new Label();
    Task bleImuTask=Task.CompletedTask;
    TaskCompletionSource<bool> retired=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    int imuCalls,clockCalls,retireCalls;
    bool BleImuShouldYield(){return yield;}
    Task RetireBleRecordingClock(){retireCalls++;return retired.Task;}
    Task PollBleImu(){imuCalls++;return Task.CompletedTask;}
    Task PollRecordingClockIfDue(){clockCalls++;return Task.CompletedTask;}
    SOURCE_LOOP
    SOURCE_AGE
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Finish(Task task){Need(task.Wait(2000),"callback did not retire");task.GetAwaiter().GetResult();}
    static void Reentrancy(bool baseline){
        var fixture=new NpBleBackgroundLoopTests();var first=fixture.PollBleBackground();var second=fixture.PollBleBackground();
        Need(!first.IsCompleted && fixture.imuCalls==0,"first tick should wait for the slow clock retirement");
        if(baseline){Need(!second.IsCompleted && fixture.retireCalls==2,"baseline did not reproduce overlapping timer callbacks");}
        else {Need(second.IsCompleted && fixture.retireCalls==1,"second timer tick entered an in-flight background cycle");Need(fixture.bleBackgroundBusy,"background ownership was lost while awaiting");}
        fixture.retired.SetResult(true);Finish(first);Finish(second);
        Need(fixture.imuCalls==(baseline?2:1) && fixture.clockCalls==(baseline?2:1),"incorrect number of sensor/clock jobs");
        if(!baseline){Need(!fixture.bleBackgroundBusy,"completed cycle did not release ownership");Finish(fixture.PollBleBackground());Need(fixture.imuCalls==2,"future polling did not resume");}
    }
    static void CancellationAndFailure(){
        var yielded=new NpBleBackgroundLoopTests();var task=yielded.PollBleBackground();yielded.yield=true;yielded.retired.SetResult(true);Finish(task);
        Need(yielded.imuCalls==0 && !yielded.bleBackgroundBusy,"foreground control did not suppress pending sensor work");
        var failed=new NpBleBackgroundLoopTests();task=failed.PollBleBackground();failed.retired.SetException(new IOException("retirement failed"));
        bool saw=false;try{task.GetAwaiter().GetResult();}catch(IOException){saw=true;}
        Need(saw && !failed.bleBackgroundBusy,"failed callback wedged the polling loop");
        failed.retired=new TaskCompletionSource<bool>();failed.retired.SetResult(true);Finish(failed.PollBleBackground());Need(failed.imuCalls==1,"polling did not resume after failure");
        var closed=new NpBleBackgroundLoopTests{closing=true};Finish(closed.PollBleBackground());Need(closed.retireCalls==0 && closed.imuCalls==0 && closed.clockCalls==0,"closed form started background work");
    }
    static void Freshness(){
        var fixture=new NpBleBackgroundLoopTests{LastBleImu=new object(),bleImuObservedAt=1000};
        fixture.RefreshBleImuAge(3999);Need(fixture.LastBleImu!=null && fixture.bleImuLabel.Text=="fresh","fresh data expired early");
        fixture.RefreshBleImuAge(4000);Need(fixture.LastBleImu==null && fixture.bleImuLabel.Visible && fixture.bleImuLabel.Text.Contains("fresh data"),"stale IMU appeared valid/current");
        fixture.LastBleImu=new object();fixture.bleImuObservedAt=4500;fixture.bleImuLabel.Text="recovered";fixture.RefreshBleImuAge(4501);Need(fixture.LastBleImu!=null && fixture.bleImuLabel.Text=="recovered","recovered IMU remained stale");
        fixture.bleImuUnsupported=true;fixture.bleImuLabel.Text="unsupported";fixture.RefreshBleImuAge(10000);Need(fixture.bleImuLabel.Text=="unsupported","age warning overwrote a capability report");
        fixture.bleImuUnsupported=false;fixture.closing=true;fixture.RefreshBleImuAge(10000);Need(fixture.LastBleImu!=null,"closed form changed display state");
        var never=new NpBleBackgroundLoopTests{LastBleImu=new object()};never.RefreshBleImuAge(10000);Need(never.LastBleImu!=null,"unset observation time was treated as a real sample");
    }
    static int Main(string[] args){try{bool baseline=args.Length>0 && args[0]=="baseline";Reentrancy(baseline);if(!baseline){CancellationAndFailure();Freshness();}Console.WriteLine("PASS: "+checks+" actual-source background "+(baseline?"overlap reproduced":"serialization/freshness/recovery")+" checks; no hardware/UI access.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
