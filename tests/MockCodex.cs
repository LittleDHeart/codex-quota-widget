using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
class MockCodex {
 static int Main() {
  var json=new JavaScriptSerializer(); bool initialized=false,acknowledged=false; string line;
  while((line=Console.ReadLine())!=null) {
   var msg=json.Deserialize<Dictionary<string,object>>(line); var method=(string)msg["method"];
   if(method=="initialize") { initialized=true; Console.WriteLine(json.Serialize(new { id=msg["id"],result=new { userAgent="quota-test" } })); }
   else if(method=="initialized" && initialized) acknowledged=true;
   else if(method=="account/rateLimits/read" && acknowledged) {
    Console.WriteLine(json.Serialize(new { id=msg["id"], result=new { rateLimitsByLimitId=new { codex=new { limitId="codex",planType="test",primary=new { usedPercent=37,windowDurationMins=300,resetsAt=2000000000 },secondary=new { usedPercent=81,windowDurationMins=10080,resetsAt=2000000000 } } } } }));
   } else { Console.WriteLine(json.Serialize(new { id=msg.ContainsKey("id")?msg["id"]:null,error=new { code=-32600,message="Unexpected method or incorrect handshake" } })); return 1; }
   Console.Out.Flush();
  }
  return 0;
 }
}
