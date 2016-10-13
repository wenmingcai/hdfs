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

(** Generation of verilog netlists *)
module DigitalLogic.Verilog

open DigitalLogic.Circuit
open DigitalLogic.Signal

(** Given an output channel, circuit name and circuit datatype writes a Verilog netlist *)
let write (f:System.IO.TextWriter) name (circuit : Circuit) = 
  let t0 = System.DateTime.Now in
  let timing s t0 t1 = System.Console.WriteLine("{0}: {1}", s, t1-t0) in

  let string_of_binop = function
    | B_add -> "+"
    | B_sub -> "-"
    | B_mulu -> "*"
    | B_muls -> "*"
    | B_and -> "&"
    | B_or -> "|"
    | B_xor -> "^"
    | B_eq -> "=="
    | B_lt -> "<"
    | B_cat -> "" 
  in

  let string_of_unop = function U_not -> "~" in

  let os (s:string) = f.Write(s) in
  os ("////////////////////////////////////////////////////////\n");
  os ("// Generated by HDFS version " ^ hdfs_version ^ "\n");
  os ("// http://code.google.com/p/hdfs/\n");
  os ("////////////////////////////////////////////////////////\n\n");

  (* outputs *)
  let outputs = circuit.Outputs in
  let output_names = List.map (fun s -> wire_name s) outputs in

  (* inputs *)
  let inputs = circuit.Inputs in
  let input_names = List.map (fun (s : Signal) -> s.name) inputs in
  
  (* inouts *)
  let inouts = circuit.Inouts in
  let inout_names = List.map (fun (s : Signal) -> s.name) inouts in
  
  let is_in_circuit (signal : Signal) = circuit.mem (signal.uid) in
  
  os ("module " ^ name ^ " (" ^ (fold_strings ", " (input_names @ output_names @ inout_names)) ^ ");\n");

  let range_of_signal s = 
    let w = width s in
    if w <> 1 then ("[" ^ string (w-1) ^ ":0] ")
    else "" 
  in

  let write_net sigtype s = os (" " ^ sigtype ^ " " ^ range_of_signal s ^ s.name ^ ";\n") in
  
  List.iter (fun x -> write_net "input" x) inputs;
  List.iter (fun s -> os (" output " ^ range_of_signal s ^ wire_name s ^ ";\n")) outputs;
  List.iter (fun s -> os (" inout " ^ range_of_signal s ^ wire_name s ^ ";\n")) inouts;

  let string_of_signal (x : Signal) = match x.signal with
    | Signal_empty     -> 
      "XXX empty XXX"
    | Signal_const    (a,w,c) -> 
      ((string w) ^ "'b" ^ c)
    | Signal_binop    (a,w,op,s0,s1) -> 
      (match op with
      | B_cat -> "{ " ^ s0.name ^ " , " ^ s1.name ^ " }"
      | B_muls -> ("( $signed(" ^ s0.name ^ ") " ^ string_of_binop op ^ " $signed(" ^ s1.name ^ ") )")
      | _ -> "( " ^ s0.name ^ " " ^ string_of_binop op ^ " " ^ s1.name ^ " )")
    | Signal_unop     (a,w,op,s) -> 
      "( " ^ string_of_unop op ^ " " ^ s.name ^ " )"
    | Signal_wire     (a,w,n,d) -> 
      "(invalid wire: size " ^ string w ^ ")" 
    | Signal_select   (a,hi,lo,s) -> 
      if hi = lo then
        if width s = 1 then (s.name)
        else (s.name ^ "[" ^ string hi ^ "]")
      else
        s.name ^ "[" ^ string hi ^ ":" ^ string lo ^ "]"
    | Signal_mux      (a,w,sel,d) -> (* only for simple 1 bit select, two state mux's *)
        ("( " ^ sel.name ^ " ? " ^ (List.head (List.tail d)).name ^ " : " ^ (List.head d).name ^ " )")
    | Signal_reg      (_) -> failwith "unexpected reg"
    | Signal_mem      (_) -> failwith "unexpected mem"
    | Signal_behave   (_) -> failwith "unexpected behave"
    | Signal_inst     (_) -> failwith "unexpected instantiation"
    | Signal_tri      (_) -> failwith "unexpected tristate"
  in
  
  let is_always (x:Signal) = match x.signal with
    | Signal_mux (a,w,sel,d) when (width sel <> 1) || (List.length d <> 2) -> true
    | Signal_reg (_)
    | Signal_behave (_) 
    | Signal_mem (_) -> true
    | _ -> false in
  
  let rec write_behave t i nodes = List.iter (write_behave_node t i) nodes
  and write_behave_node t i node = 
    match node with
    | B_if(cond, on_true, on_false) -> (
      os (i ^ "if (" ^ cond.name ^ ") begin\n");
      write_behave t (i^" ") on_true;
      os (i ^ "end else begin\n");
      write_behave t (i^" ") on_false;
      os (i ^ "end\n")
    )
    | B_switch(cond, cases) -> (
      os (i ^ "case ( " ^ cond.name ^ " ) \n");
      List.iter (fun (idx, statements) -> 
        os (i ^ " " ^ string_of_signal idx ^ ": begin\n");
        write_behave t (i^"  ") statements;
        os (i ^ " end\n");
      ) cases;
      os (i ^ "endcase\n")
    )
    | B_assign(target, expr) ->
      os (i ^ t ^ " <= " ^ expr.name ^ ";\n") 
  in
  
  let write_always (s : Signal) =  
    match s.signal with
    
    | Signal_reg(a,w,clk,rst,rstval,ena,d) -> (
      let reg_ena (ena : Signal) q (d : Signal) = 
        if ena.IsEmpty then s.name ^ " <= " ^ d.name ^ ";"
        else "if (" ^ ena.name ^ ") " ^ s.name ^ " <= " ^ d.name ^ ";" in
      if rst.IsEmpty then
        (os (" always @(posedge " ^ clk.name ^ ")\n");
         os ("   " ^ reg_ena ena s d ^ "\n"))
      else
        (os (" always @(posedge " ^ clk.name ^ ", posedge " ^ rst.name ^ ")\n");
         os ("  if (" ^ rst.name ^ ") " ^ s.name ^ " <= " ^ 
            if rstval.IsEmpty then (string (s.width) ^ "'d0;\n")
            else (rstval.name ^ ";\n")
          );
         os ("  else " ^ reg_ena ena s d ^ "\n"))
    )
    | Signal_mem(a,dw,aw,size,clk,w,we,d,r) -> 
      let array_name = apply_prefix (string (a+1)) in
      (os (" always @(posedge " ^ clk.name ^ ")\n");
       os ("  if (" ^ we.name ^ ") " ^ array_name ^ "[" ^ w.name ^ "] <= " ^ d.name ^ ";\n");
       os (" assign " ^ s.name ^ " = " ^ array_name ^ "[" ^ r.name ^ "];\n"))
       
    | Signal_mux(a,w,sel,d) -> (
      let num_cases = List.length d in
      let rec cases n (d : Signal list) = 
        match d with 
        | [] -> failwith "empty mux"
        | [a] -> 
          os ("  default: " ^ s.name ^ " <= " ^ a.name ^ ";\n")
        | hd :: tl -> (
          os ("  " ^ string n ^ ": " ^ s.name ^ " <= " ^ hd.name ^ ";\n");
          cases (n+1) tl
        )
      in
      os (" always @*\n  case ( " ^ sel.name ^ " )\n");
      cases 0 d;
      os "  endcase\n"
    )
    
    | Signal_behave(a,w,b,d) -> (
      os (" always @*\n begin\n");
      write_behave s.name "  " b;
      os (" end\n");
    )
    
    | _ -> () 
  in
  
  let string_of_generic_data t d = 
    let chktyp fn = if not (fn t) then failwith "Generic types do not match" in
    match d with
    | G_Bit d       -> chktyp g_bit_type;  (match d with 0 -> "0" | 1 -> "1" | _ -> failwith "Generic bit data may only be 1 or 0")
    //| G_Vec d       -> chktyp g_vec_type;  String.iter (fun x -> if x <> '0' && x <> '1' then failwith "Generic vector data may only be 1 or 0" d) d; "\"" ^ d ^ "\"" 
    | G_Vec d       -> 
      chktyp g_vec_type;  
      String.iter (fun x -> if x <> '0' && x <> '1' then failwith "Generic vector data may only be 1 or 0" d) d; 
      (string (String.length d)) ^ "\'b" ^ d 
    | G_Int d       -> chktyp g_int_type;  string d
    | G_String d    -> chktyp g_str_type;  "\"" ^ d ^ "\"" 
    | G_Float d     -> chktyp g_flt_type;  string d
    | G_Bool d      -> chktyp g_bool_type; if d then "1" else "0"  (* bools in verilog? dont think so *)
  in    
  
  let write_inst (signal : Signal) = 
    match signal.signal with
    | Signal_inst(a,n,m,g,io,i,o) ->
      let connect c = "." ^ fst c ^ "(" ^ (if is_in_circuit (snd c) then (snd c).name else "") ^ ")" in
      let generics = List.map (fun (n,t,d) -> 
          match d with | None -> "" | Some d -> "." ^ n ^ "(" ^ (string_of_generic_data t d) ^ ")"
        ) g in
      let generics = List.filter ((<>) "") generics in
      let generics = fold_strings ", " generics in
      let ports = (List.map connect i) @ (List.map connect o) @ (List.map connect io) in
      os (" " ^ n ^ 
        (if generics <> "" then " #(" ^ generics ^ ")" else "") ^ 
        " the_" ^ signal.name ^ " (" ^ (fold_strings ", " ports) ^ ");\n")
    | _ -> failwith ("Expecting an instantiation, got " ^ (Circuit.string_of_signal signal))
  in
  
  let write_wire (signal : Signal) = write_net (if is_always signal then "reg" else "wire") signal in
  let write_const_net (s : Signal) = os (" wire " ^ range_of_signal s ^ s.name ^ " = " ^ string_of_signal s ^ ";\n") in

  let write_mem_wires (signal : Signal) = 
    match signal.signal with
    | Signal_mem(a,dw,aw,size,clk,w,we,d,r) ->
      (write_net "wire" signal;
       os (" reg " ^ range_of_signal signal ^ (apply_prefix (string (a+1))) ^ "[0:" ^ string (size-1) ^ "];\n"))
    | _ -> failwith ("Excepting a memory, got " ^ Circuit.string_of_signal signal) 
  in
  
  let write_tristate (signal : Signal) = 
    let rec zzz n = if n = 0 then "" else "z" ^ zzz (n-1) in
    match signal.signal with 
    | Signal_tri(a,w,d) ->
      List.iter (fun ((oe : Signal),(d : Signal)) -> 
        os (" assign " ^ signal.name ^ " = " ^ oe.name ^ " ? " ^ d.name ^ " : " ^ string d.width ^ "'b" ^ (zzz d.width) ^ ";\n")
      ) d
    | _ -> failwith ("Excepting a tristate, got " ^ Circuit.string_of_signal signal) 
  in
  
  let write_decl (signal : Signal) = 
    if not signal.IsEmpty && not (circuit.IsInput signal) && not signal.IsWire && not (circuit.IsInout signal) then (
      if is_always signal then write_always signal
      else if signal.IsTristate then write_tristate signal
      else if signal.IsInst then write_inst signal
      else os (" assign " ^ signal.name ^ " = " ^ string_of_signal signal ^ ";\n")
    ) 
  in
    
  let write_connections (signal : Signal) = 
    match signal.signal with
    | Signal_wire(_,_,_,d) when not (!d).IsInst -> os (" assign " ^ signal.name ^ " = " ^ (!d).name ^ ";\n");
    | _ -> () 
  in
  
  os "\n /* constants */\n";
  List.iter write_const_net circuit.Constants;
 
  os "\n /* forward wire declarations */\n";
  List.iter write_wire circuit.Wires;
  List.iter write_wire circuit.Logic;
  List.iter write_wire circuit.Regs;
  List.iter write_mem_wires circuit.Memories;
  os "\n /* logic declarations */\n";
  List.iter write_decl circuit.Logic;
  List.iter write_decl circuit.Regs;
  List.iter write_decl circuit.Memories;
  List.iter write_inst circuit.Inst;
  os "\n /* wire connections */\n";
  List.iter write_connections circuit.Wires;
  List.iter write_connections circuit.Outputs;
  List.iter write_connections (List.filter (fun x -> not (wire_connection x).IsInst) circuit.Inouts);
  
  os "\nendmodule\n";
  
  timing "Wrote verilog in" t0 System.DateTime.Now
