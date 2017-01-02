namespace DigitalLogic

#nowarn "62"    // Using ^ for string concatenation
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

(** Generation of VHDL netlists *)

open DigitalLogic.Numeric.Conversions
open DigitalLogic.Circuit
open DigitalLogic.Signal

module Vhdl = 
begin
(** Given an output channel, circuit name and circuit datatype writes a VHDL netlist *)
let write (f:System.IO.TextWriter) name (circuit : Circuit) = 
  let t0 = System.DateTime.Now in
  let timing s t0 t1 = System.Console.WriteLine("{0}: {1}", s, t1-t0) in

  let string_of_binop = function
    | B_add -> "+"
    | B_sub -> "-"
    | B_mulu -> "*"
    | B_muls -> "*"
    | B_and -> "and"
    | B_or -> "or"
    | B_xor -> "xor"
    | B_eq -> "="
    | B_lt -> "<"
    | B_cat -> "&" in

  let string_of_unop = function U_not -> "not" in

  let os (s:string) = f.Write(s) in

  let range_of_signal s = 
    let w = width s in
    ("(" ^ string (w-1) ^ " downto 0)")
  in
  
  let type_of_signal s =
    let w = width s in
    if w <> 1 then "std_logic_vector" ^ range_of_signal s
    else "std_logic" in

  let signal_named t n s = " " ^ n ^ " : " ^ t ^ " " ^ (type_of_signal s) in
  let signal_decl t (s : Signal) = signal_named t s.name s in

  (* outputs *)
  let outputs = circuit.Outputs in
  let output_names = List.map (signal_decl "out") outputs in

  (* inputs *)
  let inputs = circuit.Inputs in
  let input_names = List.map (signal_decl "in") inputs in

  (* inouts *)
  let inouts = circuit.Inouts in
  let inout_names = List.map (signal_decl "inout") inouts in
  
  let is_in_circuit (signal : Signal) = circuit.mem signal.uid in

  (* widths of all memories declared in the circuit memories *)
  let mem_widths = Set.ofList (List.map width circuit.Memories) in

  os ("--------------------------------------------------------\n");
  os ("-- Generated by HDFS version " ^ hdfs_version ^ "\n");
  os ("-- http://code.google.com/p/hdfs/\n");
  os ("--------------------------------------------------------\n\n");

  os ("library ieee;
use ieee.std_logic_1164.all;
use ieee.numeric_std.all;

entity " ^ name ^ " is
port (
" ^ (fold_strings ";\n" (input_names @ output_names @ inout_names)) ^ ");\nend;\n\n");

  os ("architecture hdfs of " ^ name ^ " is\n");
  os ("\n -- conversion functions
 function " ^ apply_prefix "uns(a : std_logic) return unsigned is variable b : unsigned(0 downto 0); begin b(0) := a; return b; end;
 function " ^ apply_prefix "uns(a : std_logic_vector) return unsigned is begin return unsigned(a); end;
 function " ^ apply_prefix "sgn(a : std_logic) return signed is variable b : signed(0 downto 0); begin b(0) := a; return b; end;
 function " ^ apply_prefix "sgn(a : std_logic_vector) return signed is begin return signed(a); end;
 function " ^ apply_prefix "sl(a : std_logic_vector) return std_logic is begin return a(0); end;
 function " ^ apply_prefix "sl(a : unsigned) return std_logic is begin return a(0); end;
 function " ^ apply_prefix "sl(a : signed) return std_logic is begin return a(0); end;
 function " ^ apply_prefix "sl(a : boolean) return std_logic is begin if a then return '1'; else return '0'; end if; end;
 function " ^ apply_prefix "slv(a : std_logic_vector) return std_logic_vector is begin return a; end;
 function " ^ apply_prefix "slv(a : unsigned) return std_logic_vector is begin return std_logic_vector(a); end;
 function " ^ apply_prefix "slv(a : signed) return std_logic_vector is begin return std_logic_vector(a); end;
");

  let conv_tgt s = if width s = 1 then apply_prefix "sl" else apply_prefix "slv" in
  
  let string_of_signal (signal : Signal) = match signal.signal with
    | Signal_empty    -> 
      "XXX empty XXX"
    | Signal_const    (a,w,c) -> 
      if w = 1 
      then "'" ^ c ^ "'"
      else "\"" ^ c ^ "\""
    | Signal_binop    (a,w,op,s0,s1) -> 
      (match op with
      | B_muls -> ((conv_tgt signal) ^ "( " ^ apply_prefix "sgn(" ^ s0.name ^ ") " ^ string_of_binop op ^ " " ^ apply_prefix "sgn(" ^ s1.name ^ ") )")
      | _ -> (conv_tgt signal) ^ "( " ^ apply_prefix "uns(" ^ s0.name ^ ") " ^ string_of_binop op ^ " " ^ apply_prefix "uns(" ^ s1.name ^ ") )")
    | Signal_unop     (a,w,op,s) -> 
      "( " ^ string_of_unop op ^ " " ^ s.name ^ " )"
    | Signal_wire     (a,w,n,d) -> 
      "(invalid wire: " ^ string w ^ " " ^ signal.name ^ ")" 
    | Signal_select   (a,hi,lo,s) -> 
      if hi = lo then
        if s.width = 1 then s.name
        else (s.name ^ "(" ^ string hi ^ ")")
      else
        s.name ^ "(" ^ string hi ^ " downto " ^ string lo ^ ")"
    | Signal_mux      (a,w,sel,d) -> (* only for simple 1 bit select, two state mux's *)
        ((List.head d).name ^ " when " ^ sel.name ^ " = '0' else " ^ (List.head (List.tail d)).name)
    | Signal_reg      (_) -> failwith "unexpected reg"
    | Signal_mem      (_) -> failwith "unexpected mem"
    | Signal_behave   (_) -> failwith "unexpected behave"
    | Signal_inst     (_) -> failwith "unexpected instantiation"
#if INST2
    | Signal_inst2    (_) -> failwith "unexpected instantiation"
#endif
    | Signal_tri      (_) -> failwith "unexpected tristate"
  in
  
  let is_process (x : Signal) = match x.signal with 
    | Signal_mux (a,w,sel,d) when (width sel <> 1) || (List.length d <> 2) -> true
    | Signal_reg (_)
    | Signal_behave (_)
    | Signal_mem (_) -> true
    | _ -> false in

  let rec print_behave t i nodes = List.iter (print_behave_node t i) nodes
  and print_behave_node t i node = 
    match node with
    | B_if(cond, on_true, on_false) -> (
      os (i ^ "if " ^ cond.name ^ " = '1' then\n");
      print_behave t (i^" ") on_true;
      os (i ^ "else\n");
      print_behave t (i^" ") on_false;
      os (i ^ "end if;\n")
    )
    | B_switch(cond, cases) -> (
      os (i ^ "case " ^ cond.name ^ " is\n");
      List.iter (fun (idx, statements) -> 
        if width cond = 1 then os (i ^ " when '" ^ string_of_const idx ^ "' =>\n")
        else os (i ^ " when \"" ^ string_of_const idx ^ "\" =>\n");
        print_behave t (i^"  ") statements;
      ) cases;
      os (i ^ " when others => null;\n");
      os (i ^ "end case;\n")
    )
    | B_assign(target, expr) ->
      os (i ^ t ^ " <= " ^ expr.name ^ ";\n") 
  in
  
  let write_process s =  
    let w = width s in
    match s.signal with
    
    | Signal_reg(a,w,clk,rst,rstval,ena,d) -> (
      let reg_ena (ena : Signal) (q : Signal) (d : Signal) = 
        if ena.IsEmpty then q.name ^ " <= " ^ d.name ^ ";"
        else "if " ^ ena.name ^ " = '1' then " ^ q.name ^ " <= " ^ d.name ^ "; end if;" in
      if rst.IsEmpty then
        (os (" process( " ^ clk.name ^ " ) is begin\n");
         os ("  if rising_edge (" ^ clk.name ^ ") then\n");
         os ("   " ^ reg_ena ena s d ^ "\n");
         os ("  end if;\n");
         os (" end process;\n"))
      else
        (os (" process ( " ^ clk.name ^ ", " ^ rst.name ^ " ) is begin\n");
         os ("  if " ^ rst.name ^ " = '1' then " ^ s.name ^ " <= " ^ 
            (if rstval = Signal.empty then (if w=1 then "'0'" else "(others=>'0')") 
             else rstval.name) ^ 
          ";\n");
         os ("  elsif rising_edge(" ^ clk.name ^ ") then\n"); 
         os ("   " ^ reg_ena ena s d ^ "\n");
         os ("  end if;\n");
         os (" end process;\n"))
    )
    
    | Signal_mem(a,dw,aw,size,clk,w,we,d,r) -> 
      let array_name = apply_prefix (string (a+1)) in
      (os (" process (" ^ clk.name ^ ") is begin\n");
       os ("  if rising_edge(" ^ clk.name ^ ") then\n");
       os ("   if " ^ we.name ^ " = '1' then " ^ array_name ^ "(to_integer(hdfs_uns(" ^ w.name ^ "))) <= " ^ d.name ^ "; end if;\n");
       os ("  end if;\n");
       os (" end process;\n");
       os (" " ^ s.name ^ " <= " ^ array_name ^ "(to_integer(hdfs_uns(" ^ r.name ^ ")));\n")
      )

    | Signal_mux(a,w,sel,d) -> (
      let num_cases = List.length d in
      let rec cases n (d : Signal list) = 
        match d with 
        | [] -> failwith "empty mux"
        | [a] -> 
          os ("  when others => " ^ s.name ^ " <= " ^ a.name ^ ";\n")
        | hd :: tl -> (
          os ("  when \"" ^ bin_str_of_int (width sel) n ^ "\" => " ^ s.name ^ " <= " ^ hd.name ^ ";\n");
          cases (n+1) tl
        )
      in
      let triggers = List.map (fun (x : Signal) -> x.name) (List.filter (fun (s:Signal) -> not s.IsConst) (unique_signal_list d)) in
      let triggers = fold_strings ", " ((sel.name)::triggers) in    (* XXX the sel signal _must not_ be constant *)
      os (" process ( " ^ triggers ^ " ) is begin\n  case " ^ sel.name ^ " is\n");
      cases 0 d;
      os "  end case;\n end process;\n"
    )
    
    | Signal_behave(a,w,b,d) -> (
      let triggers = List.map (fun (x : Signal) -> x.name) (List.filter (fun (d:Signal) -> not d.IsConst) d) in
      let trigger_string = fold_strings ", " triggers in
      if triggers = [] then
        os (" process is begin\n")
      else
        os (" process ( " ^ trigger_string ^ " ) is begin\n");
      print_behave s.name "  " b;
      if triggers = [] then os ("  wait; -- warning - is this synthesizable?\n"); // ...yes, in synplify at least.
      os " end process;\n"      
    )
    | _ -> () in

  let string_of_float f = Printf.sprintf "%f" f in
    
  let string_of_generic_data t d = 
    let chktyp fn = if not (fn t) then failwith "Generic types do not match" in
    match d with
    | G_Bit d       -> chktyp g_bit_type;  (match d with 0 -> "'0'" | 1 -> "'1'" | _ -> failwith "Generic bit data may only be 1 or 0")
    | G_Vec d       -> chktyp g_vec_type;  String.iter (fun x -> if x <> '0' && x <> '1' then failwith "Generic vector data may only be 1 or 0" d) d; "\"" ^ d ^ "\"" 
    | G_Int d       -> chktyp g_int_type;  string d
    | G_String d    -> chktyp g_str_type;  "\"" ^ d ^ "\"" 
    | G_Float d     -> chktyp g_flt_type;  string_of_float d
    | G_Bool d      -> chktyp g_bool_type; if d then "true" else "false" 
  in    
  
  let write_inst (signal : Signal) =
    match signal.signal with
    | Signal_inst(a,n,m,g,io,i,o) ->
      (* XXXX what is going on here?  Constants fail...but they are a part of the circuit.  EH? 
         If I make them part of the ciruit...what happen? Lets try.  It'll need thinking about because outputs/inouts are different. *)
      let connect c = fst c ^ "=>" ^ (if is_in_circuit (snd c) then (snd c).name else "open") in
      let generics = List.map (fun (n,t,d) -> 
          match d with | None -> "" | Some d -> n ^ "=>" ^ (string_of_generic_data t d)
        ) g in
      let generics = fold_strings ", " generics in
      let ports = (List.map connect i) @ (List.map connect o) @ (List.map connect io) in
      os (" the_" ^ signal.name ^ " : " ^ n ^ 
        (if generics <> "" then " generic map (" ^ generics ^ ")" else "") ^ 
        " port map (" ^ (fold_strings ", " ports) ^ ");\n")
    | _ -> failwith "Expecting instance"
  in
  
  //let write_inst2 (signal : Signal) = 
  
  let string_of_generic (n,t,d) = 
    let range l r = 
      if l >= r 
      then "(" ^ string l ^ " downto " ^ string r ^ ")"
      else "(" ^ string l ^ " to " ^ string r ^ ")"
    in
    let s = 
      match t with
      | Gd_Bit x -> n ^ " : bit"
      | Gd_Bv (l,r,x) -> n ^ " : bit_vector" ^ range l r
      | Gd_Sl x -> n ^ " : std_logic"
      | Gd_Slv (l,r,x) -> n ^ " : std_logic" ^ range l r
      | Gd_Int x -> n ^ " : integer"
      | Gd_Nat x -> n ^ " : natural"
      | Gd_Pos x -> n ^ " : positive"
      | Gd_Float x -> n ^ " : real"
      | Gd_Time x -> n ^ " : time"
      | Gd_String x -> n ^ " : string"
      | Gd_Bool x -> n ^ " : boolean" 
    in
    let v = 
      match t with
      | Gd_Bit x ->       (match x with None -> "" | Some x -> "'" ^ string x ^ "'")
      | Gd_Bv (l,r,x) ->  (match x with None -> "" | Some x -> "\"" ^ x ^ "\"")
      | Gd_Sl x ->        (match x with None -> "" | Some x -> "'" ^ string x ^ "'")
      | Gd_Slv (l,r,x) -> (match x with None -> "" | Some x -> "\"" ^ x ^ "\"")
      | Gd_Int x ->       (match x with None -> "" | Some x -> string x)
      | Gd_Nat x ->       (match x with None -> "" | Some x -> string x)
      | Gd_Pos x ->       (match x with None -> "" | Some x -> string x)
      | Gd_Float x ->     (match x with None -> "" | Some x -> string_of_float x)
      | Gd_Time x ->      (match x with None -> "" | Some x -> x)
      | Gd_String x ->    (match x with None -> "" | Some x -> "\"" ^ x ^ "\"")
      | Gd_Bool x ->      (match x with None -> "" | Some x -> if x then "true" else "false")
    in
    "   " ^ s ^ (if v <> "" then " := " ^ v else "")
  in
  
  let write_component comp_set (signal : Signal) = 
    match signal.signal with
    | Signal_inst(a,n,m,g,io,i,o) ->
      if Set.contains n comp_set then comp_set
      else (
        let port_decl str signal = (" " ^ signal_decl "in" signal ^ ";\n") in
        let port_map = fold_strings ";\n" 
          ((List.map (fun c -> "  " ^ signal_named "in"  (fst c) (snd c)) i) @ 
           (List.map (fun c -> "  " ^ signal_named "out" (fst c) (snd c)) o) @ 
           (List.map (fun c -> "  " ^ signal_named "inout" (fst c) (snd c)) io)) in
        let generic_map = fold_strings ";\n" (List.map string_of_generic g) in
        os (" component " ^ n ^ " \n");
        if g <> [] then (
          os ("  generic (\n");
          os generic_map;
          os ("\n  );\n");
        );
        os ("  port (\n");
        os port_map;
        os ("\n  );\n end component;\n");
        Set.add n comp_set
      )
    | _ -> failwith "Expecting instance"
  in

#if INST2
  let write_component2 comp_set (signal : Signal) = 
    match signal.signal with
    | Signal_inst2(a,s,_,_,_,_) ->
      if Set.contains s.name comp_set then comp_set
      else (
        let string_of_range = function
          | None -> ""
          | Some(x,y) -> 
            if x < y 
            then "(" ^ string x ^ " to " ^ string y ^ ")"
            else "(" ^ string x ^ " downto " ^ string y ^ ")"
        in
        let string_of_vec_data = function 
          | BitString     (s) -> "\"" ^ s ^ "\""
          | HexString     (s) -> "X\"" ^ s ^ "\""
        in
        let string_of_generic = function
          | GenericBit    (i)   -> "bit" ^ (match i with None -> "" | Some(x) -> " := '" ^ string x ^ "'")
          | GenericSl     (i)   -> "std_logic" ^ (match i with None -> "" | Some(x) -> " := '" ^ string x ^ "'")
          | GenericInt    (i)   -> "integer" ^ (match i with None -> "" | Some(x) -> " := " ^ string x)
          | GenericNat    (i)   -> "natural" ^ (match i with None -> "" | Some(x) -> " := " ^ string x)
          | GenericPos    (i)   -> "positive" ^ (match i with None -> "" | Some(x) -> " := " ^ string x)
          | GenericFloat  (f)   -> "real" ^ (match f with None -> "" | Some(x) -> " := " ^ string_of_float x)
          | GenericTime   (s)   -> "time" ^ (match s with None -> "" | Some(x) -> " := " ^ x)
          | GenericString (s)   -> "string" ^ (match s with None -> "" | Some(x) -> " := " ^ x)  (* these could be ranged *)
          | GenericBool   (b)   -> "boolean" ^ (match b with None -> "" | Some(x) -> " := " ^ if x then "true" else "false")
          | GenericBv     (r,d) -> "bit_vector" ^ string_of_range r ^ (match d with None -> "" | Some(x) -> " := " ^ string_of_vec_data x)
          | GenericSlv    (r,d) -> "std_logic_vector" ^ string_of_range r ^ (match d with None -> "" | Some(x) -> " := " ^ string_of_vec_data x)
          | GenericUns    (r,d) -> "unsigned" ^ string_of_range r ^ (match d with None -> "" | Some(x) -> " := " ^ string_of_vec_data x)
          | GenericSgn    (r,d) -> "signed" ^ string_of_range r ^ (match d with None -> "" | Some(x) -> " := " ^ string_of_vec_data x)
        in
        let generic_spec = fold_strings ";\n" (map (fun (n,g) -> "   " ^ n ^ " : " ^ string_of_generic g) s.generics) in
        let port_spec = "" in
        os (" component " ^ s.name ^ " \n");
        if s.generics <> [] then (
          os ("  generic (\n");
          os generic_spec;
          os ("\n  );\n");
        );
        os ("  port (\n");
        os port_spec;
        os ("\n  );\n end component;\n");
        Set.add s.name comp_set
      )
    | _ -> failwith "Expecting instance2"
  in
#endif
  
  let write_tristate (signal : Signal) = 
    match signal.signal with 
    | Signal_tri(a,w,d) ->
      List.iter (fun ((oe : Signal),(d : Signal)) -> 
        os (" " ^ signal.name ^ " <= " ^ d.name ^ " when " ^ oe.name ^ " = '1' else " ^ 
          (if (width d) = 1 then "'Z'" else "(others=>'Z')") ^ ";\n")
      ) d
    | _ -> failwith ("Excepting a tristate, got " ^ Circuit.string_of_signal signal) 
  in

  let write_const (signal : Signal) = 
    if signal.IsEmpty then failwith "empty signal not expected";
    os (" constant " ^ (signal_decl "" signal) ^ " := " ^ string_of_signal signal ^ ";\n")
  in

  let write_decl (signal : Signal) = 
    if signal.IsEmpty then failwith "empty signal not expected";
    os (" signal " ^ (signal_decl "" signal) ^ ";\n")
  in
    
  let write_nodes (signal : Signal) = 
    if signal.IsEmpty then failwith "empty signal not expected"
    else if is_process signal then write_process signal
    else if signal.IsTristate then write_tristate signal
    else os (" " ^ signal.name ^ " <= " ^ string_of_signal signal ^ ";\n")
  in
    
  let write_connections (signal : Signal) = 
    match signal.signal with
    | Signal_wire(_,_,_,d) when not (!d).IsInst -> os (" " ^ signal.name ^ " <= " ^ (!d).name ^ ";\n");
    | _ -> () in
    
  let write_mem_decl (mem : Signal) = 
    match mem.signal with
     | Signal_mem(a,dw,aw,size,clk,w,we,d,r) ->
      os (" signal " ^ mem.name ^ " : " ^ type_of_signal mem ^ ";\n");
      os (" signal " ^ apply_prefix (string (a+1)) ^ " : hdfs_mem_" ^ string dw ^ "_t(0 to " ^ string (size-1) ^ ");\n")
    | _ -> failwith "Expecting a memory"
  in
 
  os "\n -- memory types\n";
  Set.iter (fun w -> 
    os (" type hdfs_mem_" ^ string w ^ "_t is array (natural range<>) of " ^ (
        if w = 1 
        then "std_logic"
        else "std_logic_vector(" ^ string (w-1) ^ " downto 0)"
      ) ^ ";\n")) mem_widths;
  
  os "\n -- components\n";
  ignore (List.fold (fun set i -> write_component set i) Set.empty circuit.Inst);
#if INST2
  ignore (fold_left (fun set i -> write_component2 set i) Set.empty circuit.Inst2);
#endif
  os "\n -- declarations\n";
  List.iter write_const circuit.Constants;
  List.iter write_decl circuit.Wires;
  List.iter write_decl circuit.Logic;
  List.iter write_decl circuit.Regs;
  List.iter write_mem_decl circuit.Memories;
  os ("\nbegin\n");
  os "\n -- logic \n";
  List.iter write_nodes circuit.Logic;
  List.iter write_nodes circuit.Regs;
  List.iter write_nodes circuit.Memories;
  List.iter write_inst circuit.Inst;
  //iter write_inst2 circuit.Inst2;
  os "\n -- wire connections\n";
  List.iter write_connections circuit.Wires;
  os "\n -- outputs\n";
  List.iter write_connections circuit.Outputs;
  os "\n -- inouts\n";
  List.iter write_connections (List.filter (fun x -> not (wire_connection x).IsInst) circuit.Inouts);
  
  os "\nend architecture;\n";
  
  timing "Wrote vhdl in" t0 System.DateTime.Now

  end