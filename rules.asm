; ===========================================================================
;  VAULTS OF ZOR - objects and rules (level file format 5)
;
;  Nothing in the game world is built in. A pack defines object types (a
;  look, solid / can be carried flags, a noun, a starting value, a name and a
;  description) and places instances of them on each level. Everything they
;  do comes from rules: the pack's rules (behaviours of object types and
;  pack-wide rules) and each level's rules. See DESIGN-OBJECTS.md.
;
;  instance record (INST, 8 bytes): x, y, type, value, p1, p2, dw text
;     x = 0: nowhere (y = 0) or carried (y = 1)
;  type record (pack, 6 bytes): look, flags, noun, value, name, desc
;     flags: bit 0 solid, bit 1 can be carried, bit 2 kept to the next level
;  rule: verb, noun, scope, conditions, actions, then 4 bytes (op,a,b,c) each
;     verb 0 every turn, 253 level start, 254 walks into, 255 arrives
;     scope 0 = any, t+1 = about THIS object of type t
;     an object argument of 0FFh means THIS
; ===========================================================================

T_EVERY	equ	0
T_START	equ	253
T_BUMP	equ	254
T_ARRIVE equ	255
V_TALK	equ	17

; ---------------------------------------------------------------- new game
newgame:
	ld	hl,(packp)
	ld	de,PK_HP
	add	hl,de
	ld	a,(hl)		; start hp
	ld	(starthp),a
	ld	(hp),a
	inc	hl
	ld	a,(hl)		; max hp
	ld	(maxhp),a
	inc	hl
	inc	hl		; (number of types)
	ld	a,(hl)		; the status line's counter and label
	ld	(statctr),a
	inc	hl
	ld	a,(hl)
	ld	(statlbl),a
	inc	hl
	ld	a,(hl)
	ld	(statlbl+1),a
	xor	a
	ld	(level),a
	ld	(nkeep),a
	ld	hl,0
	ld	(moves),hl
	ld	(points),hl
	ld	hl,FLAGS	; flags and counters
	ld	b,4+32
ng_1:	ld	(hl),0
	inc	hl
	djnz	ng_1
	ld	hl,(packp)
	ld	de,PK_VOCAB
	add	hl,de
	call	ll_ptr
	ld	(vocabp),de
	call	ll_ptr
	ld	(typep),de
	call	ll_ptr
	ld	(prules),de
	call	ll_ptr
	ld	(ptexts),de
	call	loadlevel
	jp	mainlp

; ---------------------------------------------------------------- levels
loadlevel:			; set up level (level), print its introduction
	ld	a,(level)
	add	a,a
	add	a,PK_LEVS
	call	packw
	ld	a,(hl)
	ld	(px),a
	inc	hl
	ld	a,(hl)
	ld	(py),a
	inc	hl
	ld	a,(hl)
	ld	(dir),a
	inc	hl
	inc	hl
	call	ll_ptr
	ld	(lvname),de
	call	ll_ptr
	ld	(lvintro),de
	call	ll_ptr		; the objects
	push	hl
	ex	de,hl
	xor	a
	ld	(ninst),a
	ld	a,h
	or	l
	jr	z,ll_2
	ld	a,(hl)
	inc	hl
	cp	MAXINST+1
	jr	c,ll_1
	ld	a,MAXINST
ll_1:	ld	(ninst),a
	ld	c,a
	ld	b,0
	sla	c
	rl	b
	sla	c
	rl	b
	sla	c
	rl	b
	ld	a,b
	or	c
	jr	z,ll_2
	ld	de,INST
	ldir
ll_2:	pop	hl
	call	ll_ptr
	ld	(lrules),de
	call	ll_ptr
	ld	(ltexts),de
	ld	de,MAZE		; terrain: one bit per cell, 1 = wall
	ld	c,96
ll_3:	ld	a,(hl)
	inc	hl
	ld	b,8
ll_4:	rrca
	push	af
	ld	a,0
	jr	nc,ll_5
	ld	a,40h
ll_5:	ld	(de),a
	inc	de
	pop	af
	djnz	ll_4
	dec	c
	jr	nz,ll_3
	ld	a,(nkeep)	; objects carried over from the last level
	or	a
	jr	z,ll_7
	ld	b,a
	ld	hl,KEEPBUF
ll_6:	ld	a,(ninst)
	cp	MAXINST
	jr	nc,ll_7
	push	bc
	push	hl
	call	irec
	ex	de,hl
	pop	hl
	ld	bc,8
	ldir
	ld	a,(ninst)
	inc	a
	ld	(ninst),a
	pop	bc
	djnz	ll_6
ll_7:	xor	a
	ld	(nkeep),a
	ld	(omode),a
	ld	(inlen),a
	ld	(tcol),a
	call	cellbits
	call	mkmask
	call	cls
	call	frame
	ld	a,TXTTOP-1
	ld	(trow),a
	ld	hl,M_LEVEL
	call	msg
	ld	a,(level)
	inc	a
	call	putdeca
	ld	a,':'
	call	putc
	ld	a,' '
	call	putc
	ld	hl,(lvname)
	call	msgc
	ld	hl,(lvintro)
	call	msg
	ld	a,T_START	; the level's start rules
	call	runauto
	jp	listhere

savekeep:			; remember carried objects whose type is kept
	xor	a
	ld	(nkeep),a
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	xor	a
sv_1:	push	bc
	ld	(fitem),a
	call	istate
	cp	1
	jr	nz,sv_n
	ld	a,(fitem)
	call	itrec
	inc	hl
	bit	2,(hl)
	jr	z,sv_n
	ld	a,(nkeep)
	cp	16
	jr	nc,sv_n
	add	a,a
	add	a,a
	add	a,a
	ld	hl,KEEPBUF
	call	addhla
	push	hl
	ld	a,(fitem)
	call	irec
	pop	de
	ld	bc,8
	ldir
	ld	hl,nkeep
	inc	(hl)
sv_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	sv_1
	ret

; ---------------------------------------------------------------- objects
irec:				; A = object -> HL = its record (uses DE)
	ld	l,a
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	de,INST
	add	hl,de
	ret

trec:				; A = type -> HL = its record in the pack (uses DE)
	ld	l,a
	ld	h,0
	ld	d,h
	ld	e,l
	add	hl,hl
	add	hl,de
	add	hl,hl
	ld	de,(typep)
	add	hl,de
	ret

itype:				; A = object -> A = its type
	call	irec
	inc	hl
	inc	hl
	ld	a,(hl)
	ret

itrec:				; A = object -> HL = its type's record
	call	itype
	jp	trec

iname:				; A = object -> HL = its name
	call	itrec
	ld	de,4
	add	hl,de
	ld	a,(hl)
	jp	ptext

istate:				; A = object -> A: 0 nowhere, 1 carried, 2 here,
	call	irec		;      3 in front, 4 elsewhere (uses BC, DE, HL)
	ld	a,(hl)
	or	a
	jr	nz,is_1
	inc	hl
	ld	a,(hl)
	and	1
	ret
is_1:	ld	c,a
	inc	hl
	ld	b,(hl)
	ld	a,(px)
	cp	c
	jr	nz,is_2
	ld	a,(py)
	cp	b
	ld	a,2
	ret	z
is_2:	push	bc
	call	aheadxy
	pop	bc
	ld	a,e
	cp	c
	jr	nz,is_3
	ld	a,d
	cp	b
	ld	a,3
	ret	z
is_3:	ld	a,4
	ret

setloc:				; object A goes to E = x, D = y
	push	de
	call	irec
	pop	de
	ld	(hl),e
	inc	hl
	ld	(hl),d
	ld	a,1
	ld	(idirty),a
	ret

thisnum:			; A = THIS's value (cc = 1) or P1 (cc = 2)
	ld	a,(cc)
	cp	2
	jr	nz,thisval
	ld	a,(thisi)
	cp	0FFh
	jr	z,tv_0
	call	irec
	ld	de,4
	add	hl,de
	ld	a,(hl)
	ret
thisval:			; A = THIS object's value (0 if none)
	ld	a,(thisi)
	cp	0FFh
	jr	z,tv_0
	call	irec
	inc	hl
	inc	hl
	inc	hl
	ld	a,(hl)
	ret
tv_0:	xor	a
	ret

; A = object, B = part (0 or 1 = the first) -> HL = that part of its text, NZ;
; Z if it has none. Parts are separated by 2.
ipartx:	call	irec
	ld	de,6
	add	hl,de
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	ld	a,d
	or	e
	ret	z
	ex	de,hl
	call	packoff
	ld	a,b
	or	a
	jr	z,ip_3
	dec	a
	jr	z,ip_3
	ld	b,a
ip_1:	ld	a,(hl)
	inc	hl
	or	a
	ret	z
	cp	2
	jr	nz,ip_1
	djnz	ip_1
ip_3:	ld	a,(hl)
	or	a
	ret	z
	cp	2
	ret

newinst:			; A = type, E,D = place -> a new object; carry if full
	ld	c,a
	ld	a,(ninst)
	cp	MAXINST
	ccf
	ret	c
	push	de
	push	af
	call	irec
	pop	af
	pop	de
	ld	(hl),e
	inc	hl
	ld	(hl),d
	inc	hl
	ld	(hl),c
	inc	hl
	push	hl
	ld	a,c
	call	trec
	inc	hl
	inc	hl
	inc	hl
	ld	a,(hl)		; the type's starting value
	pop	hl
	ld	(hl),a
	inc	hl
	xor	a
	ld	b,4		; p1, p2, no text
ni_1:	ld	(hl),a
	inc	hl
	djnz	ni_1
	ld	hl,ninst
	inc	(hl)
	ld	a,1
	ld	(idirty),a
	or	a
	ret

; C = type, B = state (1 carried, 2 here, 3 in front), (fmatch) = p1 wanted
; or 0FFh -> A = the first such object, carry clear; carry set if none
findany:
	ld	a,0FFh
	ld	(fmatch),a
findtype:
	ld	a,c
	ld	(ftyp),a
	ld	a,b
	ld	(fstate),a
	ld	a,(ninst)
	or	a
	jr	z,ft_no
	ld	b,a
	xor	a
ft_1:	ld	(fitem),a
	push	bc
	call	irec
	inc	hl
	inc	hl
	ld	a,(ftyp)
	cp	(hl)
	jr	nz,ft_n
	inc	hl
	inc	hl		; p1
	ld	a,(fmatch)
	cp	0FFh
	jr	z,ft_2
	cp	(hl)
	jr	nz,ft_n
ft_2:	ld	a,(fitem)
	call	istate
	ld	hl,fstate
	cp	(hl)
	jr	nz,ft_n
	pop	bc
	ld	a,(fitem)
	or	a
	ret
ft_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	ft_1
ft_no:	ld	a,0FFh
	scf
	ret

countcarried:			; A = type -> A = how many are carried
	ld	c,a
	ld	e,0
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	ld	hl,INST
cnt_1:	ld	a,(hl)
	or	a
	jr	nz,cnt_n
	inc	hl
	ld	a,(hl)
	inc	hl
	cp	1
	jr	nz,cnt_m
	ld	a,(hl)
	cp	c
	jr	nz,cnt_m
	inc	e
cnt_m:	dec	hl
	dec	hl
cnt_n:	ld	a,8
	call	addhla
	djnz	cnt_1
	ld	a,e
	ret

clearsolid:			; remove the solid objects at E,D
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	ld	hl,INST
cs_1:	ld	a,(hl)
	cp	e
	jr	nz,cs_n
	inc	hl
	ld	a,(hl)
	dec	hl
	cp	d
	jr	nz,cs_n
	push	hl
	push	de
	push	bc
	inc	hl
	inc	hl
	ld	a,(hl)
	call	trec
	inc	hl
	bit	0,(hl)
	pop	bc
	pop	de
	pop	hl
	jr	z,cs_n
	ld	(hl),0
	inc	hl
	ld	(hl),0
	dec	hl
cs_n:	ld	a,8
	call	addhla
	djnz	cs_1
	ld	a,1
	ld	(idirty),a
	ret

; Set the look and blocked bits of every maze cell from the terrain and the
; objects lying there. A solid object's look wins; otherwise the first.
cellbits:
	call	kpoll
	xor	a
	ld	(idirty),a
	ld	hl,MAZE
	ld	bc,32*24
cb_1:	ld	a,(hl)
	and	0C0h
	bit	6,a
	jr	z,cb_2
	or	20h+C_WALL
cb_2:	ld	(hl),a
	inc	hl
	dec	bc
	ld	a,b
	or	c
	jr	nz,cb_1
	call	kpoll
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	ld	hl,INST
cb_3:	push	bc
	push	hl
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	a,e
	or	a
	jr	z,cb_n
	ld	a,(hl)
	push	de
	call	trec
	ld	c,(hl)		; look
	inc	hl
	ld	b,(hl)		; flags
	pop	de
	call	caddr
	ld	a,h
	or	l
	jr	z,cb_n
	bit	0,b
	jr	z,cb_4
	set	5,(hl)
	ld	a,c
	or	a
	jr	z,cb_n
	jr	cb_5
cb_4:	ld	a,(hl)
	and	1Fh
	jr	nz,cb_n
	ld	a,c
cb_5:	ld	b,a
	ld	a,(hl)
	and	0E0h
	or	b
	ld	(hl),a
cb_n:	pop	hl
	ld	de,8
	add	hl,de
	pop	bc
	djnz	cb_3
	ret

; Which types have every-turn, arrival, walk-into and start rules (TMASK).
mkmask:	ld	hl,TMASK
	ld	b,MAXTYPES
mm_0:	ld	(hl),0
	inc	hl
	djnz	mm_0
	ld	hl,(prules)
	call	mm_list
	ld	hl,(lrules)
mm_list:
	ld	a,h
	or	l
	ret	z
	ld	b,(hl)
	inc	hl
	ld	a,b
	or	a
	ret	z
mm_1:	push	bc
	push	hl
	ld	a,(hl)
	call	trigbit
	jr	z,mm_n
	inc	hl
	inc	hl
	ld	a,(hl)		; scope
	or	a
	jr	z,mm_n
	dec	a
	cp	MAXTYPES
	jr	nc,mm_n
	ld	hl,TMASK
	call	addhla
	ld	a,(hl)
	or	c
	ld	(hl),a
mm_n:	pop	hl
	call	nextrule
	pop	bc
	djnz	mm_1
	ret

trigbit:			; A = verb -> C = its TMASK bit, Z if not a trigger
	ld	c,1
	or	a
	jr	z,tb_y
	ld	c,2
	cp	T_ARRIVE
	jr	z,tb_y
	ld	c,4
	cp	T_BUMP
	jr	z,tb_y
	ld	c,8
	cp	T_START
	jr	z,tb_y
	xor	a
	ret
tb_y:	or	1
	ret

; ---------------------------------------------------------------- texts
text:	ld	hl,(ctexts)	; A = text number of the rules being run -> HL
textin:				; HL = text table, A = number -> HL ("" for 0)
	or	a
	jr	z,tx_none
	ld	b,a
	ld	a,h
	or	l
	jr	z,tx_none
	ld	a,b
	dec	a
	ld	e,a
	ld	d,0
	ex	de,hl
	add	hl,hl
	ex	de,hl
	inc	hl
	add	hl,de
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	ex	de,hl
	jp	packoff
tx_none: ld	hl,M_EMPTY
	ret
ptext:	ld	hl,(ptexts)	; A = pack text number -> HL
	jr	textin

; ---------------------------------------------------------------- the parser's words
findword:			; HL = typed word, C = type -> A = id, 0 if unknown
	push	hl
	ex	de,hl
	ld	hl,(vocabp)
	ld	a,h
	or	l
	jr	z,fw_no
fw_1:	ld	a,(hl)
	or	a
	jr	z,fw_no
	cp	c
	inc	hl
	ld	b,(hl)
	inc	hl
	jr	nz,fw_skip
	push	de
fw_2:	ld	a,(de)
	cp	' '+1
	jr	c,fw_end
	cp	(hl)
	jr	nz,fw_mis
	inc	hl
	inc	de
	jr	fw_2
fw_end:	ld	a,(hl)
	or	a
	jr	nz,fw_mis
	pop	de
	ld	a,b
	pop	hl
	ret
fw_mis:	pop	de
fw_skip: ld	a,(hl)
	inc	hl
	or	a
	jr	nz,fw_skip
	jr	fw_1
fw_no:	xor	a
	pop	hl
	ret

bverb:				; A = built-in verb id -> its handler and turn flag
	ld	c,a
	ld	de,VOCAB
bi_1:	ld	a,(de)
	or	a
	jr	z,bi_no
bi_2:	ld	a,(de)
	inc	de
	or	a
	jr	nz,bi_2
	ld	a,(de)
	ld	b,a
	inc	de
	ld	a,(de)
	inc	de
	cp	c
	jr	z,bi_hit
	inc	de
	inc	de
	jr	bi_1
bi_hit:	ld	a,b
	ld	(tflag),a
	ld	a,(de)
	ld	l,a
	inc	de
	ld	a,(de)
	ld	h,a
	ld	(vhandler),hl
	ret
bi_no:	ld	hl,0
	ld	(vhandler),hl
	ld	a,1
	ld	(tflag),a
	ret

; ---------------------------------------------------------------- running rules
; A typed command: the objects it may be about are found (here, in front,
; carried; only those with the typed noun, if any). Rules without a scope
; see the first of them as THIS. Then each object in turn tries its type's
; rules. Level rules come before the pack's.
runverb:			; (verbid), (nounid) -> A: 0 no rule has this verb,
	call	mkcands		;   1 none applied, 2 one acted, 3 acted + continue
runcands:
	ld	a,(verbid)
	ld	(rverb),a
	xor	a
	ld	(rres),a
	ld	(contflag),a
	ld	(rmode),a
	ld	(rscope),a
	ld	a,(ncand)
	or	a
	ld	a,0FFh
	jr	z,rvb_1
	ld	a,(CANDS)
rvb_1:	ld	(thisi),a
	call	tryboth
	cp	2
	jr	z,rvb_done
	ld	a,(ncand)
	or	a
	jr	z,rvb_none
	ld	b,a
	ld	hl,CANDS
rvb_2:	push	bc
	push	hl
	ld	a,(hl)
	ld	(thisi),a
	call	itype
	inc	a
	ld	(rscope),a
	call	tryboth
	pop	hl
	pop	bc
	cp	2
	jr	z,rvb_done
	inc	hl
	djnz	rvb_2
rvb_none:
	ld	a,(rres)
	ret
rvb_done:
	ld	a,(contflag)
	or	a
	ld	a,2
	ret	z
	inc	a
	ret

mkcands:			; fill CANDS: here, then in front, then carried
	call	kpoll
	xor	a
	ld	(ncand),a
	ld	(mkany),a
	ld	c,2
	call	mk_pass
	ld	c,3
	call	mk_pass
	ld	a,(ncand)
	push	af
	ld	c,1
	call	mk_pass
	pop	bc		; B = candidates here or in front
	ld	a,b
	or	a
	ret	nz
	ld	a,(ncand)	; the noun is only something carried ("USE KEY"):
	or	a		; then what is in front is concerned too
	ret	z
	ld	a,1
	ld	(mkany),a
	ld	c,3
	call	mk_pass
	xor	a
	ld	(mkany),a
	ret
mk_pass:
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	xor	a
mk_1:	push	bc
	ld	(fitem),a
	call	istate
	pop	bc
	push	bc
	cp	c
	jr	nz,mk_n
	ld	a,(mkany)
	or	a
	jr	nz,mk_add
	ld	a,(nounid)
	or	a
	jr	z,mk_add
	ld	a,(fitem)
	call	itrec
	inc	hl
	inc	hl
	ld	a,(nounid)
	cp	(hl)
	jr	nz,mk_n
mk_add:	ld	a,(ncand)
	ld	hl,CANDS
	call	addhla
	ld	a,(fitem)
	ld	(hl),a
	ld	hl,ncand
	inc	(hl)
mk_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	mk_1
	ret

; Automatic rules: A = T_EVERY, T_ARRIVE or T_START. All that hold act: the
; rules without a scope once, then those of each object concerned (arriving:
; the objects here; otherwise every object on the level or carried).
runauto:
	ld	(rverb),a
	call	trigbit
	ld	a,c
	ld	(rbit),a
	ld	a,1
	ld	(rmode),a
	xor	a
	ld	(nounid),a
	ld	(rscope),a
	ld	a,0FFh
	ld	(thisi),a
	call	tryboth
	ld	a,(ninst)
	or	a
	ret	z
	ld	b,a
	xor	a
ra_1:	push	bc
	ld	(rinst),a
	call	kpoll		; don't miss key taps while the rules run
	ld	a,(rinst)
	call	istate
	or	a
	jr	z,ra_n
	ld	c,a
	ld	a,(rverb)
	cp	T_ARRIVE
	jr	nz,ra_2
	ld	a,c
	cp	2
	jr	nz,ra_n
ra_2:	ld	a,(rinst)
	call	itype
	ld	c,a
	ld	hl,TMASK
	call	addhla
	ld	a,(rbit)
	and	(hl)
	jr	z,ra_n		; this type has no such rules
	ld	a,c
	inc	a
	ld	(rscope),a
	ld	a,(rinst)
	ld	(thisi),a
	call	tryboth
ra_n:	ld	a,(rinst)
	inc	a
	pop	bc
	djnz	ra_1
	ret

tryboth:			; the level's rules, then the pack's -> A = 2 if one acted
	ld	hl,(ltexts)
	ld	(ctexts),hl
	ld	hl,(lrules)
	call	trylist
	cp	2
	ret	z
	ld	hl,(ptexts)
	ld	(ctexts),hl
	ld	hl,(prules)

; HL = rule list: try the rules for (rverb), (nounid) and (rscope).
; First mode: A = 2 when one acted. All mode: all are tried, A = 0.
trylist:
	xor	a
	ld	(stopflag),a
	ld	a,h
	or	l
	ret	z
	ld	b,(hl)
	inc	hl
	ld	a,b
	or	a
	ret	z
tl_1:	push	bc
	ld	(rptr),hl
	ld	a,(rverb)
	cp	(hl)
	jr	nz,tl_n
	inc	hl
	ld	a,(hl)		; noun 0 = any
	or	a
	jr	z,tl_2
	ld	c,a
	ld	a,(nounid)
	cp	c
	jr	nz,tl_n
tl_2:	ld	a,1
	ld	(rres),a
	inc	hl
	ld	a,(rscope)
	cp	(hl)
	jr	nz,tl_n
	ld	hl,(rptr)
	call	tryrule
	jr	nz,tl_n
	ld	a,(rmode)
	or	a
	jr	z,tl_hit
	ld	a,(stopflag)	; all mode: go on unless a rule said stop
	or	a
	jr	z,tl_n
	pop	bc
	ld	a,2
	ret
tl_hit:	pop	bc
	ld	a,2
	ret
tl_n:	ld	hl,(rptr)
	call	nextrule
	pop	bc
	djnz	tl_1
	xor	a
	ret

nextrule:			; HL = rule -> HL = the next one
	push	hl
	inc	hl
	inc	hl
	inc	hl
	ld	a,(hl)
	inc	hl
	add	a,(hl)
	add	a,a
	add	a,a
	add	a,5
	pop	hl
	jp	addhla

tryrule:			; HL = rule: if all conditions hold, do the actions (Z)
	inc	hl
	inc	hl
	inc	hl
	ld	b,(hl)
	inc	hl
	ld	c,(hl)
	inc	hl
	ld	a,b
	or	a
	jr	z,tr_act
tr_c:	push	bc
	call	evalcond
	pop	bc
	ret	nz
	djnz	tr_c
tr_act:	ld	a,c
	or	a
	jr	z,tr_end
	ld	b,c
tr_a:	push	bc
	call	doact
	pop	bc
	djnz	tr_a
tr_end:	ld	a,(idirty)
	or	a
	call	nz,cellbits
	xor	a
	ret

getargs:			; HL -> op,a,b,c: copy to cop..cc, HL past them
	ld	de,cop
	ld	bc,4
	ldir
	ret

evalcond:			; HL = condition -> Z if it holds; HL past it
	call	getargs
	push	hl
	ld	a,(cop)
	and	7Fh
	cp	NCOND
	jr	c,ec_0
	xor	a
ec_0:	ld	hl,CONDTAB
	call	ptrtab
	call	jphl
	ld	a,0
	jr	nz,ec_1
	inc	a
ec_1:	ld	b,a
	ld	a,(cop)
	rlca
	and	1
	xor	b
	pop	hl
	cp	1
	ret

doact:				; HL = action: do it; HL past it
	call	getargs
	push	hl
	ld	a,(cop)
	cp	NACT
	jr	c,da_0
	xor	a
da_0:	ld	hl,ACTTAB
	call	ptrtab
	call	jphl
	pop	hl
	ret

jphl:	jp	(hl)

; ---- arguments
iarg:	ld	a,(ca)		; the object in argument a -> A; carry if none
ires:	cp	0FFh		; A = object argument (0FFh = THIS) -> A; carry if none
	jr	nz,ia_1
	ld	a,(thisi)
	cp	0FFh
	scf
	ret	z
ia_1:	ld	hl,ninst
	cp	(hl)
	ccf
	ret

xyarg:				; A = x (0FFh = THIS's P1,P2), B = y -> E,D; carry if none
	cp	0FFh
	jr	nz,xy_1
	ld	a,(thisi)
	cp	0FFh
	scf
	ret	z
	call	irec
	ld	de,4
	add	hl,de
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	or	a
	ret
xy_1:	ld	e,a
	ld	d,b
	or	a
	ret

aheadxy:			; E,D = the cell in front of the player
	ld	a,(dir)
	call	getvec
	ld	a,(px)
	add	a,b
	ld	e,a
	ld	a,(py)
	add	a,c
	ld	d,a
	ret

flagbit:			; A = flag 0-31 -> HL = its byte, A = its bit mask
	ld	c,a
	rrca
	rrca
	rrca
	and	3
	ld	hl,FLAGS
	call	addhla
	ld	a,c
	and	7
	ld	b,a
	inc	b
	ld	a,80h
fb_1:	rlca
	djnz	fb_1
	ret

cptr:				; HL = counter (ca), 16-bit low byte first
	ld	a,(ca)
	and	15
	add	a,a
	ld	hl,COUNTERS
	jp	addhla

cvsn:				; HL = counter (ca) value - N (cb low, cc high); carry if less
	call	cptr
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a
	ld	a,(cb)
	ld	e,a
	ld	a,(cc)
	ld	d,a
	or	a
	sbc	hl,de
	ret

; ---- conditions: Z if true ----------------------------------------------------
CONDTAB: dw	c_false,c_carried,c_here,c_present,c_exists,c_at,c_flag,c_cntge
	dw	c_cntlt,c_iahead,c_chance,c_has,c_ahead,c_near,c_valge,c_vallt
	dw	c_hpge,c_facing,c_typehere,c_is,c_match,c_self
NCOND	equ	($-CONDTAB)/2
c_self:	ld	a,(thisi)	; THIS is object a (behaviour of one placed object)
	ld	b,a
	ld	a,(ca)
	cp	b
	ret
c_true:	xor	a
	ret
c_false: or	1
	ret
c_carried:
	ld	c,1
cst:	push	bc
	call	iarg
	pop	bc
	jp	c,c_false
	push	bc
	call	istate
	pop	bc
	cp	c
	ret
c_here:	ld	c,2
	jr	cst
c_iahead:
	ld	c,3
	jr	cst
c_present:
	call	iarg
	jp	c,c_false
	call	istate
	cp	1
	ret	z
	cp	2
	ret
c_exists:
	call	iarg
	jp	c,c_false
	call	istate
	or	a
	jp	z,c_false
	jp	c_true
c_at:	ld	a,(cb)
	ld	b,a
	ld	a,(ca)
	call	xyarg
	jp	c,c_false
	ld	a,(px)
	cp	e
	ret	nz
	ld	a,(py)
	cp	d
	ret
c_flag:	ld	a,(ca)
	call	flagbit
	and	(hl)
	jp	z,c_false
	jp	c_true
c_cntge:			; counter >= N (16-bit)
	call	cvsn
	jp	c,c_false
	jp	c_true
c_cntlt:			; counter < N (16-bit)
	call	cvsn
	jp	c,c_true
	jp	c_false
gecmp:	cp	b		; Z if A >= B
	jp	c,c_false
	jp	c_true
ltcmp:	cp	b		; Z if A < B
	jp	c,c_true
	jp	c_false
c_chance:
	call	rnd
	ld	a,(ca)
	ld	b,a
	ld	a,l
	jp	ltcmp
c_has:	ld	a,(ca)		; carrying at least b (0 = 1) objects of type a
	call	countcarried
	ld	c,a
	ld	a,(cb)
	or	a
	jr	nz,ch_1
	inc	a
ch_1:	ld	b,a
	ld	a,c
	jp	gecmp
c_ahead:
	ld	a,(cb)
	ld	b,a
	ld	a,(ca)
	call	xyarg
	jp	c,c_false
	push	de
	call	aheadxy
	pop	hl
	ld	a,e
	cp	l
	ret	nz
	ld	a,d
	cp	h
	ret
c_near:	call	iarg		; here, or next to the player
	jp	c,c_false
	push	af
	call	istate
	pop	bc
	cp	2
	ret	z
	cp	3
	ret	z
	cp	4
	jp	nz,c_false
	ld	a,b
	call	irec
	ld	a,(px)
	sub	(hl)
	jr	nc,cn_1
	neg
cn_1:	ld	c,a
	inc	hl
	ld	a,(py)
	sub	(hl)
	jr	nc,cn_2
	neg
cn_2:	add	a,c
	ld	b,2
	jp	ltcmp
c_valge:
	call	ivalue
	jp	c,c_false
	jp	gecmp
c_vallt:
	call	ivalue
	jp	c,c_false
	jp	ltcmp
ivalue:				; A = value of object (ca), B = (cb); carry if none
	call	iarg
	ret	c
	call	irec
	inc	hl
	inc	hl
	inc	hl
	ld	a,(cb)
	ld	b,a
	ld	a,(hl)
	or	a
	ret
c_hpge:	ld	a,(ca)
	ld	b,a
	ld	a,(hp)
	jp	gecmp
c_facing:
	ld	b,3
	jr	cty
c_typehere:
	ld	b,2
cty:	ld	a,(ca)
	ld	c,a
	call	findany
	jp	c,c_false
	jp	c_true
c_is:	ld	a,(thisi)	; THIS is of type a
	cp	0FFh
	jp	z,c_false
	call	itype
	ld	b,a
	ld	a,(ca)
	cp	b
	ret
c_match:			; carrying a type a object whose P1 is THIS's P1
	call	thisp1
	jp	c,c_false
	ld	a,(ca)
	ld	c,a
	ld	b,1
	call	findtype
	jp	c,c_false
	jp	c_true
thisp1:				; (fmatch) = THIS's P1; carry if no THIS
	ld	a,(thisi)
	cp	0FFh
	scf
	ret	z
	call	irec
	ld	de,4
	add	hl,de
	ld	a,(hl)
	ld	(fmatch),a
	or	a
	ret

; ---- actions -------------------------------------------------------------------
ACTTAB:	dw	a_none,a_say,a_speak,a_set,a_clear,a_get,a_drop,a_destroy
	dw	a_put,a_swap,a_cell,a_goto,a_cset,a_cadd,a_csub,a_score
	dw	a_hurt,a_heal,a_give,a_take,a_win,a_die,a_inv,a_look
	dw	a_cont,a_next,a_become,a_setv,a_addv,a_subv,a_sayit,a_speakit
	dw	a_create,a_usem,a_stop
NACT	equ	($-ACTTAB)/2
a_stop:	ld	a,1		; no more rules for this object and trigger
	ld	(stopflag),a
	ret
a_none:	ret
a_say:	ld	a,(ca)
	call	text
	jp	msg
a_speak:
	ld	a,(ca)
	call	text
	ld	a,1
	ld	(keepview),a
	jp	speak
a_set:	ld	a,(ca)
	call	flagbit
	or	(hl)
	ld	(hl),a
	ret
a_clear:
	ld	a,(ca)
	call	flagbit
	cpl
	and	(hl)
	ld	(hl),a
	ret
a_get:	call	iarg
	ret	c
	ld	de,0100h	; carried
	jp	setloc
a_drop:	call	iarg
	ret	c
	ld	b,a
	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	ld	a,b
	jp	setloc
a_destroy:
	call	iarg
	ret	c
	ld	de,0
	jp	setloc
a_put:	call	iarg
	ret	c
	push	af
	ld	a,(cc)
	ld	b,a
	ld	a,(cb)
	call	xyarg
	jr	c,ap_no
	pop	af
	jp	setloc
ap_no:	pop	af
	ret
a_swap:	call	iarg		; swap the places of two objects
	ret	c
	push	af
	ld	a,(cb)
	call	ires
	jr	c,ap_no
	call	irec
	pop	af
	push	hl
	call	irec
	pop	de
	ld	b,2
as_1:	ld	a,(de)
	ld	c,(hl)
	ld	(hl),a
	ld	a,c
	ld	(de),a
	inc	hl
	inc	de
	djnz	as_1
	ld	a,1
	ld	(idirty),a
	ret
a_cell:	ld	a,(cb)		; cell x,y becomes 0 floor, 1 wall, 2 open
	ld	b,a		; (floor, and its solid objects are removed)
	ld	a,(ca)
	call	xyarg
	ret	c
	push	de
	call	caddr
	pop	de
	ld	a,h
	or	l
	ret	z
	ld	a,(cc)
	cp	1
	jr	z,acl_w
	res	6,(hl)
	cp	2
	call	z,clearsolid
	jr	acl_d
acl_w:	set	6,(hl)
acl_d:	ld	a,1
	ld	(idirty),a
	ret
a_goto:	ld	a,(cb)		; the player goes to x,y (if it isn't blocked)
	ld	b,a
	ld	a,(ca)
	call	xyarg
	ret	c
	push	de
	call	caddr
	pop	de
	ld	a,h
	or	l
	ret	z
	bit	5,(hl)
	ret	nz
	set	7,(hl)
	ld	a,e
	ld	(px),a
	ld	a,d
	ld	(py),a
	ld	a,1
	ld	(moved),a
	ret
a_cset:	call	cptr		; counter a = N (b low, c high)
	ld	a,(cb)
	ld	(hl),a
	inc	hl
	ld	a,(cc)
	ld	(hl),a
	ret
a_cadd:	call	cptr		; counter a += b (c = 1: THIS's value, 2: P1), 16-bit
	call	cnum
	add	a,(hl)
	ld	(hl),a
	inc	hl
	ret	nc
	inc	(hl)
	ret	nz
	ld	(hl),0FFh	; went past 65535: stay at 65535
	dec	hl
	ld	(hl),0FFh
	ret
a_csub:	call	cptr		; counter a -= b (c = 1: THIS's value, 2: P1), 16-bit
	call	cnum
	ld	b,a
	ld	a,(hl)
	sub	b
	ld	(hl),a
	inc	hl
	ret	nc
	ld	a,(hl)
	sub	1
	ld	(hl),a
	ret	nc
	xor	a		; went below 0: stay at 0
	ld	(hl),a
	dec	hl
	ld	(hl),a
	ret
addsat:	add	a,(hl)		; 8-bit (object values)
	jr	nc,ac_1
	ld	a,255
ac_1:	ld	(hl),a
	ret
subsat:	ld	b,a
	ld	a,(hl)
	sub	b
	jr	nc,ac_1
	xor	a
	jr	ac_1
cnum:	ld	a,(cc)		; A = (cb), or THIS's value (cc = 1) / P1 (cc = 2); keeps HL
	or	a
	ld	a,(cb)
	ret	z
	push	hl
	call	thisnum
	pop	hl
	ret
a_score:			; points += a + 256 b (c = 1: THIS's value, 2: P1)
	ld	a,(cc)
	or	a
	jr	z,asc_1
	call	thisnum
	ld	e,a
	ld	d,0
	jr	asc_2
asc_1:	ld	a,(ca)
	ld	e,a
	ld	a,(cb)
	ld	d,a
asc_2:	ld	hl,(points)
	add	hl,de
	ld	(points),hl
	ret
numarg:				; A = (ca), or THIS's value (cc = 1) / P1 (cc = 2)
	ld	a,(cc)
	or	a
	jp	nz,thisnum
	ld	a,(ca)
	ret
a_hurt:	call	numarg		; lose a hp (b = 1: a random 1..a)
	or	a
	ret	z
	ld	b,a
	ld	a,(cb)
	or	a
	jp	z,hurt
	push	bc
	call	rnd
	pop	bc
	ld	a,l
	and	7Fh
	call	modb
	inc	a
	ld	b,a
	jp	hurt
a_heal:	call	numarg		; gain a hp (0 = fully)
	ld	b,a
	ld	a,(maxhp)
	ld	c,a
	ld	a,b
	call	orelse
	ld	b,a
	call	heal_by
	ld	hl,M_HPIS
	call	msg
	ld	a,(hp)
	jp	putdeca
a_give:	ld	a,(cb)		; b (0 = 1) new type a objects, carried
	or	a
	jr	nz,ag_1
	inc	a
ag_1:	ld	b,a
ag_2:	push	bc
	ld	a,(ca)
	ld	de,0100h
	call	newinst
	pop	bc
	ret	c
	djnz	ag_2
	ret
a_take:	ld	a,(cb)		; b (0 = 1) carried type a objects vanish
	or	a
	jr	nz,atk_1
	inc	a
atk_1:	ld	b,a
atk_2:	push	bc
	ld	a,(ca)
	ld	c,a
	ld	b,1
	call	findany
	pop	bc
	ret	c
	push	bc
	ld	de,0
	call	setloc
	pop	bc
	djnz	atk_2
	ret
a_win:	jp	win
a_die:	ld	a,(ca)
	call	text
	call	msg
	jp	death
a_inv:	jp	act_inv
a_look:	jp	act_look
a_cont:	ld	a,1
	ld	(contflag),a
	ret
a_next:	jp	nextlevel
a_become:			; object a turns into type b
	call	iarg
	ret	c
	call	irec
	inc	hl
	inc	hl
	ld	a,(cb)
	ld	(hl),a
	ld	a,1
	ld	(idirty),a
	ret
a_setv:	call	vptrx
	ret	c
	ld	a,(cb)
	ld	(hl),a
	ret
a_addv:	call	vptrx
	ret	c
	ld	a,(cb)
	jp	addsat
a_subv:	call	vptrx
	ret	c
	ld	a,(cb)
	jp	subsat
vptrx:	call	iarg		; HL = value of object (ca); carry if none
	ret	c
	call	irec
	inc	hl
	inc	hl
	inc	hl
	or	a
	ret
a_sayit:			; show part b of object a's text
	call	iarg
	ret	c
	ld	b,a
	ld	a,(cb)
	ld	c,b
	ld	b,a
	ld	a,c
	call	ipartx
	ret	z
	jp	msg
a_speakit:			; "<NAME> SAYS:" and part b of its text as speech
	call	iarg
	ret	c
	ld	(fitem),a
	call	iname
	call	msg
	ld	hl,M_SAYS
	call	msgc
	ld	a,(cb)
	ld	b,a
	ld	a,(fitem)
	call	ipartx
	ret	z
	ld	a,1
	ld	(keepview),a
	jp	speak
a_create:			; a new type a object at b,c (b = 0FFh: THIS's P1,P2)
	ld	a,(cc)
	ld	b,a
	ld	a,(cb)
	call	xyarg
	ret	c
	ld	a,(ca)
	jp	newinst
a_usem:				; a carried type a object with THIS's P1 is used up
	call	thisp1
	ret	c
	ld	a,(ca)
	ld	c,a
	ld	b,1
	call	findtype
	ret	c
	ld	de,0
	jp	setloc

; ---------------------------------------------------------------- built-in verbs
; Each runs only when no rule took the command (or one said "continue").
candin:				; A = state -> A = first candidate in it that the
	ld	(fstate),a	;   command means (no noun: one that can be carried)
	ld	a,(ncand)
	or	a
	jr	z,ci_no
	ld	b,a
	ld	hl,CANDS
ci_1:	push	bc
	push	hl
	ld	a,(hl)
	ld	(fitem),a
	call	istate
	ld	hl,fstate
	cp	(hl)
	jr	nz,ci_n
	ld	a,(nounid)
	or	a
	jr	nz,ci_hit
	ld	a,(fitem)
	call	itrec
	inc	hl
	bit	1,(hl)
	jr	nz,ci_hit
ci_n:	pop	hl
	pop	bc
	inc	hl
	djnz	ci_1
ci_no:	ld	a,0FFh
	ret
ci_hit:	pop	hl
	pop	bc
	ld	a,(fitem)
	ret

act_take:
	ld	a,2
	call	candin
	cp	0FFh
	jr	nz,tk_1
	ld	hl,M_NOTAKE
	ld	a,(nounid)
	or	a
	jp	z,msg
	ld	hl,M_NOSEE
	jp	msg
tk_1:	ld	(fitem),a
	call	itrec
	inc	hl
	bit	1,(hl)
	ld	hl,M_CANTCARRY
	jp	z,msg
	ld	a,(fitem)
	ld	de,0100h
	call	setloc
	call	cellbits
	ld	hl,M_TAKE
	call	msg
	ld	a,(fitem)
	call	iname
	call	msgc
	ld	a,'.'
	jp	putc

act_drop:
	ld	hl,M_DROPWHAT
	ld	a,(nounid)
	or	a
	jp	z,msg
	ld	a,1
	call	candin
	cp	0FFh
	ld	hl,M_NOTHAVE
	jp	z,msg
	ld	b,a
	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	ld	a,b
	call	setloc
	call	cellbits
	ld	hl,M_DROPPED
	jp	msg

act_look:
	ld	a,(nounid)	; LOOK <noun>
	or	a
	jr	z,lk_0
	ld	a,(ncand)
	or	a
	ld	hl,M_NOSEE
	jp	z,msg
	ld	a,(CANDS)
	jp	describe
lk_0:	ld	hl,M_FACE
	call	msg
	ld	a,(dir)
	ld	hl,DIRNAME
	call	ptrtab
	call	msgc
	ld	a,'.'
	call	putc
	ld	a,3		; something you can see in front?
	call	firstvis
	cp	0FFh
	jr	z,lk_1
	call	describe
	jr	lk_here
lk_1:	call	aheadxy
	call	caddr
	ld	a,h
	or	l
	jr	z,lk_w
	ld	a,(hl)
	ld	hl,M_LPASS
	bit	6,a
	jr	z,lk_m
lk_w:	ld	hl,M_LWALL
lk_m:	call	msg
lk_here:
	jp	listhere

describe:			; A = object: its own text, its type's, or its name
	ld	(fitem),a
	ld	b,1
	call	ipartx
	jp	nz,msg
	ld	a,(fitem)
	call	itrec
	ld	de,5
	add	hl,de
	ld	a,(hl)
	or	a
	jr	z,dsc_1
	call	ptext
	jp	msg
dsc_1:	ld	hl,M_YOUSEE
	call	msg
	ld	a,(fitem)
	call	iname
	call	msgc
	ld	a,'.'
	jp	putc

firstvis:			; A = state -> A = first object in it that has a look
	ld	(fstate),a
	ld	a,(ninst)
	or	a
	jr	z,fv_no
	ld	b,a
	xor	a
fv_1:	ld	(fitem),a
	push	bc
	call	istate
	ld	hl,fstate
	cp	(hl)
	jr	nz,fv_n
	ld	a,(fitem)
	call	itrec
	ld	a,(hl)
	or	a
	jr	z,fv_n
	pop	bc
	ld	a,(fitem)
	ret
fv_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	fv_1
fv_no:	ld	a,0FFh
	ret

listhere:			; "YOU SEE <NAME>." for each object lying here
	ld	a,(ninst)	; (not invisible ones, nor wall or door faces)
	or	a
	ret	z
	ld	b,a
	xor	a
lh_1:	ld	(fitem),a
	push	bc
	call	istate
	cp	2
	jr	nz,lh_n
	ld	a,(fitem)
	call	itrec
	ld	a,(hl)
	cp	C_LADDER
	jr	c,lh_n
	ld	a,(fitem)	; its kind has arrival rules: they say what it is
	call	irec
	inc	hl
	inc	hl
	ld	a,(hl)
	ld	hl,TMASK
	call	addhla
	bit	1,(hl)
	jr	nz,lh_n
	ld	hl,M_YOUSEE
	call	msg
	ld	a,(fitem)
	call	iname
	call	msgc
	ld	a,'.'
	call	putc
lh_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	lh_1
	ret

act_inv:			; what is carried, one line per type
	ld	hl,M_CARRY
	call	msg
	xor	a
	ld	(nprint),a
	ld	a,(ninst)
	or	a
	jr	z,iv_e
	ld	b,a
	xor	a
iv_1:	ld	(fitem),a
	push	bc
	call	istate
	cp	1
	jr	nz,iv_n
	ld	a,(fitem)
	call	itype
	ld	(ftyp),a
	call	earlier
	jr	z,iv_n
	ld	a,(ftyp)
	call	countcarried
	ld	(fcount),a
	call	newline
	ld	a,(fcount)
	cp	2
	jr	c,iv_2
	call	putdeca
	ld	hl,M_TIMES
	call	msgc
iv_2:	ld	a,(fitem)
	call	iname
	call	msgc
	ld	hl,nprint
	inc	(hl)
iv_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	iv_1
iv_e:	ld	a,(nprint)
	or	a
	ret	nz
	ld	hl,M_NOTHING
	jp	msg
earlier:			; Z if an object before (fitem) is carried and of type (ftyp)
	ld	a,(fitem)
	or	a
	jr	z,ea_no
	ld	b,a
	ld	hl,INST
ea_1:	ld	a,(hl)
	or	a
	jr	nz,ea_n
	inc	hl
	ld	a,(hl)
	inc	hl
	cp	1
	jr	nz,ea_m
	ld	a,(ftyp)
	cp	(hl)
	ret	z
ea_m:	dec	hl
	dec	hl
ea_n:	ld	a,8
	call	addhla
	djnz	ea_1
ea_no:	or	1
	ret

act_talk:
	ld	hl,M_NOONE
	jp	msg

act_help:
	ld	hl,M_HELP
	jp	msg

act_score:
	ld	hl,M_SCORE
	call	msg
	ld	hl,(points)
	call	putdec
	ld	hl,M_MOVES
	call	msgc
	ld	hl,(moves)
	jp	putdec

act_quit:
	ld	hl,M_QUIT
	call	msg
	call	waitkey
	and	0DFh
	cp	'Y'
	jp	z,exitdos
	ld	hl,M_OK
	jp	msg

; ---------------------------------------------------------------- moving
domove:				; B,C = step: move if the way is free
	ld	a,(px)
	add	a,b
	ld	e,a
	ld	a,(py)
	add	a,c
	ld	d,a
	push	de
	call	caddr
	pop	de
	ld	a,h
	or	l
	jp	z,mv_wall
	bit	5,(hl)
	jr	nz,mv_block
	push	hl
	push	de
	ld	a,(mvback)
	or	a
	jr	nz,mv_bk
	ld	a,1		; forward: glide 1/4, 1/2, 3/4 of the way
	call	animstep
	ld	a,2
	call	animstep
	ld	a,3
	call	animstep
mv_bk:	pop	de
	pop	hl
	ld	a,e
	ld	(px),a
	ld	a,d
	ld	(py),a
	set	7,(hl)
	ld	a,(mvback)
	or	a
	jr	z,arrive
	ld	a,3		; backward: the same frames seen from the new cell
	call	animstep
	ld	a,2
	call	animstep
	ld	a,1
	call	animstep
	jr	arrive
mv_block:
	bit	6,(hl)
	jp	nz,mv_wall
	xor	a		; walking into a solid object: its rules
	ld	(ncand),a
	ld	a,(ninst)
	or	a
	jr	z,mv_none
	ld	b,a
	xor	a
mv_1:	push	bc
	ld	(fitem),a
	push	de
	call	irec
	pop	de
	ld	a,(hl)
	cp	e
	jr	nz,mv_n
	inc	hl
	ld	a,(hl)
	cp	d
	jr	nz,mv_n
	push	de
	ld	a,(fitem)
	call	itrec
	pop	de
	inc	hl
	bit	0,(hl)
	jr	z,mv_n
	ld	a,(ncand)
	ld	hl,CANDS
	call	addhla
	ld	a,(fitem)
	ld	(hl),a
	ld	hl,ncand
	inc	(hl)
mv_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	mv_1
	xor	a
	ld	(nounid),a
	ld	a,T_BUMP
	ld	(verbid),a
	call	runcands
	cp	2
	ret	nc
	ld	a,V_TALK	; nothing special: then it's like talking to it
	ld	(verbid),a
	call	runcands
	cp	2
	ret	nc
mv_none:
	ld	hl,M_BLOCK
	jp	msg
mv_wall:
	ld	hl,M_BUMP
	jp	msg

arrive:				; arriving in a cell: what is here, arrival rules
	xor	a
	ld	(tele),a
ar_1:	xor	a
	ld	(moved),a
	call	listhere
	ld	a,T_ARRIVE
	call	runauto
	ld	a,(moved)	; a rule moved the player: arrive there too,
	or	a		; but only once (no chains of teleporters)
	ret	z
	ld	a,(tele)
	or	a
	ret	nz
	inc	a
	ld	(tele),a
	jr	ar_1

gturn:				; the end of a turn: every-turn rules
	ld	hl,(moves)
	inc	hl
	ld	(moves),hl
	ld	a,T_EVERY
	jp	runauto

; ---------------------------------------------------------------- hit points
orelse:				; A = A if non-zero, else C
	or	a
	ret	nz
	ld	a,c
	ret

modb:				; A = A mod B (B > 0)
	cp	b
	ret	c
	sub	b
	jr	modb

hurt:				; lose B hit points, maybe die
	ld	a,(hp)
	sub	b
	jr	nc,hu_1
	xor	a
hu_1:	ld	(hp),a
	ld	hl,M_LOSE
	call	msg
	ld	a,b
	call	putdeca
	ld	hl,M_HPD
	call	msgc
	ld	a,(hp)
	or	a
	ret	nz
	jp	death

heal_by:			; add B hit points up to the maximum
	ld	a,(maxhp)
	ld	c,a
	ld	a,(hp)
	add	a,b
	jr	c,hb_1
	cp	c
	jr	c,hb_2
hb_1:	ld	a,c
hb_2:	ld	(hp),a
	ret

; ---------------------------------------------------------------- the end
death:	call	status
	ld	hl,M_DEAD
	call	msg
	call	waitenter
	jp	gotitle

nextlevel:			; on to the next level, or the end of the game
	call	render
	call	drawmap
	call	status
	ld	a,(level)
	inc	a
	ld	b,a
	ld	hl,(packp)
	ld	de,PK_NLEV
	add	hl,de
	ld	a,(hl)
	cp	b
	jr	z,win_end
	jr	c,win_end
	ld	hl,M_LVDONE
	call	msg
	call	waitenter
	call	savekeep
	ld	hl,level
	inc	(hl)
	ld	sp,STACK
	call	loadlevel
	jp	mainlp

win:	call	render
	call	drawmap
	call	status
win_end:
	ld	a,PK_VICT
	call	packw
	call	msg
	ld	hl,M_WIN
	call	msg
	ld	hl,M_SCORE
	call	msg
	ld	hl,(points)
	call	putdec
	ld	hl,M_MOVES
	call	msgc
	ld	hl,(moves)
	call	putdec
	ld	hl,M_ENTER
	call	msg
	call	waitenter
	jp	gotitle

; ---------------------------------------------------------------- vocabulary
; built-in verbs: word, 0, takes-a-turn flag, verb id, handler. Every other
; verb (OPEN, ATTACK, DRINK...) belongs to the pack.
VOCAB:	db	"F",0,1,1
	dw	act_fwd
	db	"FORWARD",0,1,1
	dw	act_fwd
	db	"GO",0,1,1
	dw	act_fwd
	db	"WALK",0,1,1
	dw	act_fwd
	db	"B",0,1,2
	dw	act_back
	db	"BACK",0,1,2
	dw	act_back
	db	"L",0,1,3
	dw	act_left
	db	"LEFT",0,1,3
	dw	act_left
	db	"R",0,1,4
	dw	act_right
	db	"RIGHT",0,1,4
	dw	act_right
	db	"T",0,1,5
	dw	act_turn
	db	"TURN",0,1,5
	dw	act_turn
	db	"AROUND",0,1,6
	dw	act_around
	db	"G",0,1,7
	dw	act_take
	db	"GET",0,1,7
	dw	act_take
	db	"TAKE",0,1,7
	dw	act_take
	db	"TALK",0,1,17
	dw	act_talk
	db	"SPEAK",0,1,17
	dw	act_talk
	db	"ASK",0,1,17
	dw	act_talk
	db	"GREET",0,1,17
	dw	act_talk
	db	"HELLO",0,1,17
	dw	act_talk
	db	"X",0,0,12
	dw	act_look
	db	"LOOK",0,0,12
	dw	act_look
	db	"EXAMINE",0,0,12
	dw	act_look
	db	"I",0,0,13
	dw	act_inv
	db	"INV",0,0,13
	dw	act_inv
	db	"INVENTORY",0,0,13
	dw	act_inv
	db	"H",0,0,14
	dw	act_help
	db	"HELP",0,0,14
	dw	act_help
	db	"?",0,0,14
	dw	act_help
	db	"S",0,0,15
	dw	act_score
	db	"SCORE",0,0,15
	dw	act_score
	db	"Q",0,0,16
	dw	act_quit
	db	"QUIT",0,0,16
	dw	act_quit
	db	"DROP",0,1,18
	dw	act_drop
	db	0

; ---- messages (each line at most 30 characters, 13 = new line) ------------
M_HELP:	db	"ARROWS: UP/DOWN = STEP FWD/BK",13
	db	"LEFT/RIGHT = TURN. OR TYPE:",13
	db	"F B L R TURN TAKE DROP TALK",13
	db	"LOOK INV SCORE QUIT.",13
	db	"TRY ANYTHING!",0
M_LEVEL:	db	"LEVEL ",0
M_LVDONE:	db	"LEVEL COMPLETE. PRESS ENTER",13
	db	"TO GO ON.",0
M_TIMES:	db	" X ",0
M_DISK:	db	"CANNOT LOAD THE LEVEL FILE",0
M_NOTFND: db	"LEVEL FILE NOT FOUND ON ANY DRIVE",0
M_ERRNO:	db	" - TRSDOS ERROR ",0
M_BIG:	db	"LEVEL FILE TOO BIG FOR MEMORY",0
M_BADPK:	db	"NOT A VAULTS OF ZOR LEVEL FILE",0
M_ANYKEY:	db	"PRESS ANY KEY TO RETURN TO TRSDOS.",0
DEFNAME:	db	"ZOR/LEV",13
PKSIG:	db	"ZORPACK",5
M_ECHO:	db	"> ",0
M_UNK:	db	"I DON'T UNDERSTAND THAT.",0
M_CANT:	db	"YOU CAN'T DO THAT... YET.",0
M_NOSEE: db	"I DON'T SEE IT HERE.",0
M_CANTCARRY: db	"YOU CAN'T CARRY THAT.",0
M_DROPWHAT: db	"DROP WHAT?",0
M_NOTHAVE: db	"YOU'RE NOT CARRYING IT.",0
M_DROPPED: db	"DROPPED.",0
M_EMPTY: db	0
M_BUMP:	db	"YOU BUMP INTO SOLID STONE.",0
M_BLOCK: db	"SOMETHING BLOCKS THE WAY.",0
M_YOUSEE: db	"YOU SEE ",0
M_TAKE:	db	"YOU TAKE ",0
M_NOTAKE: db	"THERE IS NOTHING HERE TO TAKE.",0
M_LOSE:	db	"YOU LOSE ",0
M_HPD:	db	" HP.",0
M_HPIS:	db	"HP NOW ",0
M_SAYS:	db	" SAYS:",0
M_NOONE: db	"THERE IS NO ONE TO TALK TO.",0
M_FACE:	db	"YOU FACE ",0
M_LWALL: db	"A WALL OF COLD, DAMP STONE.",0
M_LPASS: db	"THE PASSAGE LEADS ON.",0
M_CARRY: db	"YOU CARRY:",0
M_NOTHING: db	"NOTHING.",0
M_SCORE: db	"SCORE ",0
M_MOVES: db	"  MOVES ",0
M_QUIT:	db	"QUIT TO TRSDOS? (Y/N)",0
M_OK:	db	"VERY WELL.",0
M_DEAD:	db	"YOU HAVE DIED IN THE DARK.",13
	db	"PRESS ENTER.",0
M_WIN:	db	"*** YOU HAVE WON ***",0
M_ENTER: db	"PRESS ENTER.",0
S_HP:	db	" HP:",0
S_SC:	db	" SC:",0
