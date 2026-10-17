package com.rightpad.capture;
import java.io.*;
import java.util.*;
// Isolated stdin/stdout adapter around the unchanged production Java authority.
public final class LayoutPeer {
    public static void main(String[] args) throws Exception {
        var f=new RemoteLayoutTests.Fixture();
        var reader=new BufferedReader(new InputStreamReader(System.in));
        String line;
        while((line=reader.readLine())!=null) {
            if(line.equals("reset")) { f=new RemoteLayoutTests.Fixture(); System.out.println("ok"); continue; }
            if(line.equals("fail")) { f.fail=true; System.out.println("ok"); continue; }
            var parts=line.split(" ",2); long now=Long.parseLong(parts[0]);
            byte[] bytes=Base64.getDecoder().decode(parts[1]);
            var command=RemoteLayoutProtocol.decode(bytes,bytes.length);
            if(command==null) throw new AssertionError("invalid C# encoding");
            var response=f.session.handle(command,now,true,false);
            System.out.println(Base64.getEncoder().encodeToString(RemoteLayoutProtocol.encode(response))+"|"+f.writes+"|"+f.publications+"|"+f.live.get("xbox.b.slide").x);
        }
    }
}
