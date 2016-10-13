#light "off"
(*
  HDFS Digital Logic Hardware Design (HDFS.dll)
  Copyright (C) 2006 Andy Ray.

  This library is free software; you can redistribute it and/or
  modify it under the terms of the GNU Lesser General Public
  License as published by the Free Software Foundation; either
  version 2.1 of the License, or (at your option) any later version.

  This library is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
  Lesser General Public License for more details.

  You should have received a copy of the GNU Lesser General Public
  License along with this library; if not, write to the Free Software
  Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
*)

(** Generation of an elaborated hardware designs in f# *)
module DigitalLogic.Fsharp

open DigitalLogic
open Circuit
open Signal

(** Given an output channel, circuit name and circuit datatype writes the circuit as hdfs (elaborated) *)
let write (f:System.IO.TextWriter) name (circuit : Circuit) = 
  let t0 = System.DateTime.Now in
  let timing s t0 t1 = System.Console.WriteLine("{0}: {1}", s, t1-t0) in
  let os (s:string) = f.Write(s) in

  os ("////////////////////////////////////////////////////////\n");
  os ("// Generated by HDFS version " ^ hdfs_version ^ "\n");
  os ("// http://code.google.com/p/hdfs/\n");
  os ("////////////////////////////////////////////////////////\n\n");

  (* schedule *)
  let schedule = 
    scheduler 
      (fun (s : Signal) -> if s.IsWire || s.IsInst then [] else s.dependants)
      (circuit.Logic @ circuit.Regs @ circuit.Memories @ circuit.Constants)
      (circuit.Outputs @ circuit.Inputs @ circuit.Wires @ circuit.Inst)
  in

  let outputs = circuit.Outputs in
  let inputs = circuit.Inputs in
  let wires = circuit.Wires in
  
  os (
"open DigitalLogic
open Design

let " ^ name ^ " () = 
");

  let list_of_signals signals = "[ " ^ (fold_strings "; " (List.map (fun (s : Signal) -> s.name) signals)) ^ " ]" in

  let rec string_of_signal (x : Signal) = match x.signal with
    | Signal_empty    -> "empty"
    | Signal_const    (a,w,c) -> "(constb \"" ^ c ^ "\")"
    | Signal_binop    (a,w,op,s0,s1) -> "(" ^ s0.name ^ " " ^ string_of_binop op ^ " " ^ s1.name ^ ")"
    | Signal_unop     (a,w,op,s) -> "(" ^ string_of_unop op ^ " " ^ s.name ^ ")"
    | Signal_wire     (a,w,n,d) -> "(wire " ^ string w ^ ")" 
    | Signal_mux      (a,w,sel,d) -> "(mux " ^ sel.name ^ " " ^ list_of_signals d ^ ")"
    | Signal_select   (a,hi,lo,s) -> "(select " ^ s.name ^ " " ^ string hi ^ " " ^ string lo ^ ")"
    | Signal_reg      (a,w,clk,rst,rstval,ena,d) -> 
      "(reg " ^ clk.name ^ " " ^ 
        (if rst = Signal.empty then "empty" else rst.name) ^ " " ^ 
        (if rstval = Signal.empty then "empty" else string_of_signal rstval) ^ " " ^ 
        (if ena = Signal.empty then "empty" else ena.name) ^ " " ^ d.name ^ ")"
    | Signal_mem      (a,dw,aw,size,clk,w,we,d,r) -> "(memory " ^ string size ^ " " ^ clk.name ^ " " ^ w.name ^ " " ^ we.name ^ " " ^ d.name ^ " " ^ r.name ^ ")"
    | Signal_behave   (a,w,b,d) -> failwith "unexpected behave"
    | Signal_inst     (a,n,m,g,io,i,o) -> 
      let connect (c : string * Signal) = ("(\"" ^ fst c ^ "\" ==> " ^ (snd c).name ^ ")") in
      ("(instgio \"" ^ name ^ "\" " ^ " [] [ " ^ (* doesnt support generics ... *)
        (fold_strings "; " (List.map connect io)) ^ " ] [ " ^
        (fold_strings "; " (List.map connect i)) ^ " ] [ " ^
        (fold_strings "; " (List.map connect o)) ^ " ])")
    | Signal_tri      (a,w,d) ->
      ("(tristate [ ^ " ^ (fold_strings "; " (List.map (fun ((oe : Signal),(d : Signal)) -> "(" ^ oe.name ^ ", " ^ d.name ^ ")") d)) ^ " ])")
  in
  
  let write_input (signal : Signal) = os (" let " ^ signal.name ^ " = input \"" ^ signal.name ^ "\" " ^ string (signal.width) ^ " in\n") in
  let write_wire (signal : Signal) = 
    if (wire_name signal) = "" then
      os (" let " ^ signal.name ^ " = " ^ string_of_signal signal ^ " in\n") 
    else
      os (" let " ^ signal.name ^ " = \"" ^ string_of_signal signal ^ "\" -- " ^ signal.name ^ " in\n") in
  let write_clock signal = if signal <> clock then write_wire signal in
  let write_reset signal = if signal <> reset then write_wire signal in
  let write_output (signal : Signal) = os ("  output \"" ^ signal.name ^ "\" " ^ (wire_connection signal).name ^ ";\n") in

  let write_decl (signal : Signal) = 
    match signal.signal with
    | Signal_behave(_,_,b,_) -> (
      os (" let _ = behave [\n");
      print_behave f "  " b;
      os (" ] in\n")
    ) 
    | Signal_inst(_) -> os (" let _ = " ^ string_of_signal signal ^ " in\n")
    | _ -> os (" let " ^ signal.name ^ " = " ^ string_of_signal signal ^ " in\n");
  in

  let write_connection (signal : Signal) = 
    match signal.signal with
    | Signal_wire(_,_,_,d) -> (
      if not (!d).IsBehave && not (!d).IsInst then (* connections are made automatically during compilation ... doesnt work anywaymore ... *)
        os (" " ^ signal.name ^ " <== " ^ (!d).name ^ ";\n")
    )
    | _ -> () 
  in

  os "\n (* inputs. *)\n";
  List.iter write_input inputs;
  os "\n (* wires. *)\n";
  List.iter write_wire wires;
  os "\n (* logic. *)\n";
  List.iter (fun (s : Signal) -> if not (s.IsEmpty) then write_decl s) schedule;
  os "\n (* instantiations. *)\n";
  List.iter (fun (s : Signal) -> write_decl s) circuit.Inst;
  os "\n (* connections. *)\n";
  List.iter (fun (s : Signal) -> if not s.IsEmpty then write_connection s) circuit.Wires;
  os "\n (* outputs *)\n [\n";
  List.iter write_output outputs;
  os " ]\n";

  timing "Wrote f# in" t0 System.DateTime.Now
