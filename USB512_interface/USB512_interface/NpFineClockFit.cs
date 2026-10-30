using System;
using System.Collections.Generic;

namespace USB512_interface
{
    // Pure math over an already validated common counter/PC context. This does
    // not parse packets, establish clock continuity or calibrate physical ADC time.
    internal static class NpFineClockFit
    {
        internal const int MaximumPoints=256;
        internal const decimal NumericalGuardUs=1m;
        internal sealed class Point
        {
            internal decimal RxCounter,TxCounter,SendUs,ReceiveUs,MinRequestCounter,MinReplyCounter;
        }
        internal sealed class Result
        {
            internal bool Qualified,RateBoundDeterminesPrecision,Extrapolated;
            internal string Error;
            internal int PointCount;
            internal decimal? LowerUs,UpperUs,RateLowerUsPerCounter,RateUpperUsPerCounter;
            internal decimal ObservationCounterStart,ObservationCounterEnd,ExtrapolationBeforeCounter,ExtrapolationAfterCounter;
            internal decimal HostAnchorHalfWidthUs,NumericalGuardUs;
            internal string BoundsSource="all causal affine constraints; device-counter UART minima; counter-read +1 tick; shared host-anchor uncertainty once; outward integer rounding";
            internal string Assumption="One positive affine PC/counter relation across the source target and observation window. Continuous domain, source association and clock stability are caller obligations; no Gaussian or delay-symmetry assumption.";
        }
        struct Row {internal decimal X,Y,Send,Receive;}
        struct Vertex {internal decimal A,B;internal Vertex(decimal a,decimal b){A=a;B=b;}}
        static Result Fail(string reason,int count=0){return new Result {Qualified=false,Error=reason,PointCount=count,NumericalGuardUs=NumericalGuardUs};}
        static int Compare(Point a,Point b)
        {
            int c=a.RxCounter.CompareTo(b.RxCounter);if(c!=0)return c;c=a.TxCounter.CompareTo(b.TxCounter);if(c!=0)return c;
            c=a.SendUs.CompareTo(b.SendUs);if(c!=0)return c;c=a.ReceiveUs.CompareTo(b.ReceiveUs);if(c!=0)return c;
            c=a.MinRequestCounter.CompareTo(b.MinRequestCounter);return c!=0?c:a.MinReplyCounter.CompareTo(b.MinReplyCounter);
        }
        static void Add(List<Vertex> p,Vertex v){if(p.Count==0||p[p.Count-1].A!=v.A||p[p.Count-1].B!=v.B)p.Add(v);}
        // Keep intercept + slope*x <= value. Coordinates are centered before
        // clipping, so large absolute epochs never enter a decimal product.
        static List<Vertex> Clip(List<Vertex> polygon,decimal x,decimal value,bool lower)
        {
            var output=new List<Vertex>();if(polygon.Count==0)return output;
            Vertex previous=polygon[polygon.Count-1];decimal before=previous.A+previous.B*x-value;if(lower)before=-before;
            foreach(var current in polygon){decimal after=current.A+current.B*x-value;if(lower)after=-after;
                if((before<=0)!=(after<=0)){
                    decimal fraction=before/(before-after);
                    Add(output,new Vertex(previous.A+(current.A-previous.A)*fraction,previous.B+(current.B-previous.B)*fraction));
                }
                if(after<=0)Add(output,current);previous=current;before=after;
            }
            if(output.Count>1&&output[0].A==output[output.Count-1].A&&output[0].B==output[output.Count-1].B)output.RemoveAt(output.Count-1);
            return output;
        }
        static List<Vertex> Polygon(List<Row> rows,decimal lowRate,decimal highRate)
        {
            var first=rows[0];decimal aMin=Math.Min(first.Send-lowRate*first.X,first.Send-highRate*first.X);
            decimal aMax=Math.Max(first.Receive-lowRate*first.Y,first.Receive-highRate*first.Y);
            var p=new List<Vertex>();if(aMin>aMax)return p;
            Add(p,new Vertex(aMin,lowRate));Add(p,new Vertex(aMax,lowRate));Add(p,new Vertex(aMax,highRate));Add(p,new Vertex(aMin,highRate));
            foreach(var row in rows){p=Clip(p,row.X,row.Send,true);p=Clip(p,row.Y,row.Receive,false);if(p.Count==0)break;}
            return p;
        }
        static void Extrema(List<Vertex> polygon,decimal low,decimal high,out decimal minimum,out decimal maximum)
        {
            minimum=Decimal.MaxValue;maximum=Decimal.MinValue;
            foreach(var v in polygon){minimum=Math.Min(minimum,v.A+v.B*low);maximum=Math.Max(maximum,v.A+v.B*high);}
        }
        internal static Result Fit(IEnumerable<Point> observations,decimal targetLow,decimal targetHigh,decimal nominalUsPerCounter,decimal hostAnchorHalfWidthUs=0m)
        {
            if(observations==null)return Fail("missing_points");
            if(nominalUsPerCounter<=0||hostAnchorHalfWidthUs<0||targetLow>targetHigh)return Fail("invalid_rate_anchor_or_target_bounds");
            var points=new List<Point>();
            try{
                foreach(var p in observations){
                    if(points.Count==MaximumPoints)return Fail("point_limit_exceeded",points.Count+1);
                    if(p==null||p.TxCounter<p.RxCounter||p.ReceiveUs<=p.SendUs||p.MinRequestCounter<0||p.MinReplyCounter<0)return Fail("invalid_point_counter_time_or_uart_minimum",points.Count);
                    points.Add(new Point {RxCounter=p.RxCounter,TxCounter=p.TxCounter,SendUs=p.SendUs,ReceiveUs=p.ReceiveUs,MinRequestCounter=p.MinRequestCounter,MinReplyCounter=p.MinReplyCounter});
                }
                if(points.Count<2)return Fail("insufficient_points",points.Count);
                points.Sort(Compare);decimal counterOrigin=points[0].RxCounter,pcOrigin=points[0].SendUs;
                decimal lastRx=counterOrigin,lastTx=points[0].TxCounter;
                foreach(var p in points){pcOrigin=Math.Min(pcOrigin,p.SendUs);lastRx=Math.Max(lastRx,p.RxCounter);lastTx=Math.Max(lastTx,p.TxCounter);}
                if(lastRx-counterOrigin<=1m)return Fail("insufficient_counter_span",points.Count);
                var rows=new List<Row>();foreach(var p in points)rows.Add(new Row {
                    X=p.RxCounter-counterOrigin+1m-p.MinRequestCounter,Y=p.TxCounter-counterOrigin+p.MinReplyCounter,
                    Send=p.SendUs-pcOrigin,Receive=p.ReceiveUs-pcOrigin});
                // Eliminating the intercept yields the exact data-only slope
                // interval: lower_i(b) <= upper_j(b), for every i,j. Slope zero
                // is included only as the conservative closure of b>0.
                decimal dataLow=0m,dataHigh=Decimal.MaxValue;
                foreach(var lower in rows)foreach(var upper in rows){
                    decimal x=lower.X-upper.Y,delta=lower.Send-upper.Receive;
                    if(x==0){if(delta>0)return Fail("inconsistent_causal_constraints",points.Count);}
                    else if(x>0)dataLow=Math.Max(dataLow,delta/x);else dataHigh=Math.Min(dataHigh,delta/x);
                }
                if(dataHigh==Decimal.MaxValue)return Fail("unbounded_rate_or_missing_span",points.Count);
                if(dataHigh<=0||dataLow>dataHigh)return Fail("no_positive_causal_affine_mapping",points.Count);
                decimal rateLow=Math.Max(dataLow,nominalUsPerCounter/2m),rateHigh=Math.Min(dataHigh,nominalUsPerCounter*2m);
                if(rateLow>rateHigh||rateLow<=0)return Fail("causal_rate_outside_nominal_sanity_range",points.Count);
                var data=Polygon(rows,dataLow,dataHigh);
                if(data.Count==0)return Fail("inconsistent_causal_polygon",points.Count);
                decimal low,high;
                Extrema(data,targetLow-counterOrigin,targetHigh-counterOrigin,out low,out high);
                decimal limitedLow=low,limitedHigh=high;
                if(rateLow!=dataLow||rateHigh!=dataHigh){
                    var nominal=Polygon(rows,rateLow,rateHigh);if(nominal.Count==0)return Fail("inconsistent_nominal_polygon",points.Count);
                    Extrema(nominal,targetLow-counterOrigin,targetHigh-counterOrigin,out limitedLow,out limitedHigh);
                }
                if(low>high)return Fail("invalid_target_interval",points.Count);
                var result=new Result {Qualified=true,PointCount=points.Count,RateLowerUsPerCounter=dataLow,RateUpperUsPerCounter=dataHigh,
                    ObservationCounterStart=counterOrigin,ObservationCounterEnd=lastTx,ExtrapolationBeforeCounter=Math.Max(0m,counterOrigin-targetLow),
                    ExtrapolationAfterCounter=Math.Max(0m,targetHigh-lastTx),HostAnchorHalfWidthUs=hostAnchorHalfWidthUs,NumericalGuardUs=NumericalGuardUs};
                result.Extrapolated=result.ExtrapolationBeforeCounter!=0||result.ExtrapolationAfterCounter!=0;
                result.RateBoundDeterminesPrecision=limitedLow-low>NumericalGuardUs||high-limitedHigh>NumericalGuardUs;
                if(result.RateBoundDeterminesPrecision){result.Qualified=false;result.Error="nominal_rate_bound_would_determine_precision";return result;}
                // Use data-only extrema even when the difference is below the
                // numerical guard. No nominal-rate cutoff supplies precision.
                result.LowerUs=Math.Floor(pcOrigin+low-hostAnchorHalfWidthUs-NumericalGuardUs);
                result.UpperUs=Math.Ceiling(pcOrigin+high+hostAnchorHalfWidthUs+NumericalGuardUs);
                return result;
            }catch(OverflowException){return Fail("decimal_overflow",points.Count);}
            catch(ArithmeticException){return Fail("decimal_arithmetic_failure",points.Count);}
        }
    }
}
