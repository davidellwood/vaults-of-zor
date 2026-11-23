; ===========================================================================
;  VAULTS OF ZOR  -  a maze adventure for the TRS-80 Model I (16K+, Level II)
;  Z80 machine code. Assemble with zmac:   zmac zor.asm
;  Run in trs80gp:                         trs80gp -m1 zout/zor.cmd
;
;  Screen (64 x 16 characters) is split into three windows:
;
;    col 0-31            col 32   col 34-63
;   +-------------------+  |  +------------------------+
;   |                   |  |  | MAP (15x7 cells)       |  rows 0-6
;   |  3D VIEW          |  |  |-- status line ---------|  row 7
;   |  32x16 chars      |  |  | TEXT OUTPUT (7 lines)  |  rows 8-14
;   |  (64x48 pixels)   |  |  | > COMMAND INPUT        |  row 15
;   +-------------------+  |  +------------------------+
;
;  The 3D view is assembled from pre-rasterised wall segments (see gen.py)
;  into an off-screen buffer, sprites are masked on top, then the buffer is
;  copied to video RAM in one go so the view never flickers.
; ===========================================================================

VIDEO	equ	3C00h		; Model I video RAM
KBD	equ	002Bh		; ROM: scan keyboard, A = key or 0

TXTCOL	equ	34		; text window
TXTW	equ	30
TXTTOP	equ	8
TXTBOT	equ	14
MAPCOL	equ	34		; map window
STROW	equ	7		; status line

; maze cell codes (low 7 bits; bit 7 = cell has been seen)
C_FLOOR	equ	0
C_WALL	equ	1
C_DOOR	equ	2
C_LADDER equ	3
C_GUARD	equ	4
C_KEY	equ	5
C_SWORD	equ	6
C_POTION equ	7
C_GOLD	equ	8
C_AMULET equ	9
C_SIGN	equ	10		; spaces with game logic
C_TRAP	equ	11
C_TELE	equ	12
C_FOUNT	equ	13
C_PLATE	equ	14
C_SECRET equ	15		; looks like wall, walk straight through
C_NPC	equ	16		; 16-18: characters (hermit, merchant, ghost)
C_THING	equ	19		; drawn for a script item (never stored in the map)
MAXITEMS equ	32		; script items per level
MAXOBJ	equ	64		; parameter records per level

FRAMEDLY equ	60		; animation pause: 16 keyboard polls x this loop (~17 ms)

; TRSDOS 2.3 (Model I) entry points
@EXIT	equ	402Dh		; return to TRSDOS READY
@FSPEC	equ	441Ch		; HL = filespec text, DE = FCB
@OPEN	equ	4424h		; DE = FCB, HL = 256-byte buffer, B = LRL
@CLOSE	equ	4428h		; DE = FCB
@READ	equ	4436h		; DE = FCB, next record into buffer
HIGH$	equ	4049h		; top of free memory
CMDBUF	equ	43h		; high byte of the DOS command line buffer (4318h)

; level pack header offsets (all pointers are offsets from the pack start)
PK_LEN	equ	8		; dw total length
PK_NLEV	equ	10		; db number of levels
PK_STATS equ	11		; db starthp,maxhp,heal,hit,ghit,goldmin,goldrange
PK_TITLE equ	18		; dw -> 1024-byte title screen
PK_VICT	equ	20		; dw -> victory text
PK_NAMES equ	22		; dw x 11 -> names: keys 0-3, sword, potion, gold, amulet, 3 characters
PK_VOCAB equ	44		; dw -> the script vocabulary
PK_LEVS	equ	46		; dw x n -> level records
; level record: db x,y,dir,guardhp ; dw name ; dw intro ; dw objects ;
;               dw items ; dw rules ; dw texts (0 = none) ;
;               768 bytes maze (one byte per cell)
; objects: db count, then count x 8-byte records (see objat)

	org	5200h

entry:	ld	sp,STACK
	ld	(tailp),hl
	xor	a
	ld	(errcode),a
	ld	(noshow),a
	ld	(kqn),a
	ld	(keepview),a	; TRSDOS passes the command line tail in HL
	ld	hl,0ACE1h
	ld	(seed),hl
	call	findpack

; ---------------------------------------------------------------- title
gotitle:
	ld	sp,STACK
	ld	a,PK_TITLE
	call	packw
	ld	de,VIDEO
	ld	bc,1024
	ldir
tt1:	call	waitkey
	cp	01h		; BREAK: back to TRSDOS
	jp	z,exitdos
	cp	0Dh
	jr	nz,tt1

; ---------------------------------------------------------------- new game
newgame:
	ld	hl,(packp)	; copy the game rules out of the pack
	ld	de,PK_STATS
	add	hl,de
	ld	de,starthp
	ld	bc,7
	ldir
	ld	a,(starthp)
	ld	(hp),a
	xor	a
	ld	(hassw),a
	ld	(potions),a
	ld	(level),a
	ld	(amulets),a
	ld	hl,0
	ld	(gold),hl
	ld	(moves),hl
	ld	(points),hl	; script state: flags, counters and points
	ld	(FLAGS),hl
	ld	(FLAGS+2),hl
	ld	(COUNTERS),hl
	ld	(COUNTERS+2),hl
	ld	(COUNTERS+4),hl
	ld	(COUNTERS+6),hl
	ld	hl,(packp)	; the script vocabulary, if any
	ld	de,PK_VOCAB
	add	hl,de
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	ld	a,d
	or	e
	jr	z,ng_v
	ld	hl,(packp)
	add	hl,de
	ex	de,hl
ng_v:	ld	(vocabp),de
	call	loadlevel

mainlp:	ld	a,(keepview)	; a speech bubble stays up until the next action
	or	a
	call	z,render
	xor	a
	ld	(keepview),a
	call	kpoll
	call	drawmap
	call	status
	call	kpoll
	call	command
	jr	mainlp

; ===========================================================================
;  Keyboard and random numbers
; ===========================================================================
inkey:	push	bc
	push	de
	push	hl
	call	KBD
	pop	hl
	pop	de
	pop	bc
	ret

waitkey:			; next key: from the type-ahead queue, else the keyboard
	ld	a,(kqn)
	or	a
	jr	z,wk_1
	push	bc
	push	de
	ld	a,(KQ)
	push	af
	ld	hl,KQ+1
	ld	de,KQ
	ld	bc,15
	ldir
	ld	hl,kqn
	dec	(hl)
	pop	af
	pop	de
	pop	bc
	ret
wk_1:	call	rnd		; wait for a key, stirring the RNG meanwhile
	call	inkey
	or	a
	jr	z,wk_1
	ret

waitenter:
	call	waitkey
	cp	0Dh
	jr	nz,waitenter
	ret

rnd:				; 16-bit xorshift, result in HL (and seed)
	ld	hl,(seed)
	ld	a,h
	rra
	ld	a,l
	rra
	xor	h
	ld	h,a
	ld	a,l
	rra
	ld	a,h
	rra
	xor	l
	ld	l,a
	xor	h
	ld	h,a
	ld	(seed),hl
	ret

; ===========================================================================
;  Level packs
; ===========================================================================
; Pick the level pack: a file named on the command line ("ZOR MAZES/LEV"),
; else the pack built into this /CMD file, else ZOR/LEV from disk.
findpack:
	ld	hl,BUILTIN	; the disk buffer lives just past the built-in pack
	ld	de,(BUILTIN+PK_LEN)
	add	hl,de
	inc	h		; round up to a page boundary
	ld	l,0
	ld	(packbuf),hl
	ld	hl,(tailp)
	ld	a,h
	cp	CMDBUF
	jr	nz,fp_1
	ld	a,(hl)
	cp	'A'
	jr	c,fp_1
	cp	'Z'+1
	jr	c,loadpack
fp_1:	ld	a,(BUILTIN+PK_NLEV)
	or	a
	jr	z,fp_2
	ld	hl,BUILTIN
	ld	(packp),hl
	ret
fp_2:	ld	hl,DEFNAME
				; fall into loadpack
loadpack:			; HL = filespec. Loads the pack into packbuf.
	ld	de,FCB
	call	@FSPEC
	ld	a,13h		; "illegal file name"
	jp	nz,diskerr
	ld	hl,SECBUF
	ld	de,FCB
	ld	b,0		; 256-byte records
	call	@OPEN
	jr	z,lp_ok
	cp	2Ah		; LRL mismatch is only a warning
	jp	nz,diskerr
lp_ok:	ld	hl,(packbuf)
	ld	(dest),hl
lp_rd:	ld	de,FCB
	call	@READ
	jr	nz,lp_eof
	ld	hl,(dest)	; room for another sector?
	ld	de,256
	add	hl,de
	ex	de,hl
	call	memtop
	or	a
	sbc	hl,de
	jr	c,lp_big
	ld	hl,SECBUF
	ld	de,(dest)
	ld	bc,256
	ldir
	ld	(dest),de
	jr	lp_rd
lp_eof:	ld	de,FCB
	call	@CLOSE
	ld	hl,(packbuf)	; check signature and length
	ld	de,PKSIG
	ld	b,8
lp_sig:	ld	a,(de)
	cp	(hl)
	jr	nz,lp_bad
	inc	hl
	inc	de
	djnz	lp_sig
	ld	hl,(packbuf)
	ld	(packp),hl
	ld	de,(dest)	; loaded bytes must cover the stated length
	ex	de,hl
	or	a
	sbc	hl,de		; HL = bytes loaded
	ex	de,hl
	ld	hl,(packbuf)
	ld	bc,PK_LEN
	add	hl,bc
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a		; HL = stated length
	ex	de,hl
	or	a
	sbc	hl,de
	jr	c,lp_bad
	ld	hl,(packp)
	ld	de,PK_NLEV
	add	hl,de
	ld	a,(hl)
	or	a
	jr	z,lp_bad
	ret
lp_big:	ld	hl,M_BIG
	jr	fatal
lp_bad:	ld	hl,M_BADPK
	jr	fatal

memtop:				; HL = highest usable address
	ld	hl,(HIGH$)
	ld	a,h
	cp	80h		; implausible value: assume 48K
	ret	nc
	ld	hl,0FF00h
	ret

diskerr:			; A = TRSDOS error code
	ld	(errcode),a
	ld	hl,M_DISK
	cp	18h		; "file not in directory"
	jr	nz,fatal
	xor	a
	ld	(errcode),a
	ld	hl,M_NOTFND
fatal:				; show message HL on a clear screen, then exit
	push	hl
	call	cls
	ld	a,1
	ld	(omode),a
	ld	hl,VIDEO+64*6+4
	ld	(vptr),hl
	pop	hl
	call	msgc
	ld	a,(errcode)
	or	a
	jr	z,fa_1
	ld	hl,M_ERRNO
	call	msgc
	ld	a,(errcode)
	call	putdeca
fa_1:	ld	hl,VIDEO+64*8+4
	ld	(vptr),hl
	ld	hl,M_ANYKEY
	call	msgc
	call	waitkey
exitdos:
	call	cls
	jp	@EXIT

packw:				; HL = pack address of the word at header offset A
	ld	hl,(packp)
	push	hl
	call	addhla
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	pop	hl
	add	hl,de
	ret

loadlevel:			; set up level (level) and print its introduction
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
	ld	a,(hl)
	ld	(ghpmax),a
	inc	hl
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	(lvname),de
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	(lvintro),de
	ld	e,(hl)		; copy the object records into OBJS
	inc	hl
	ld	d,(hl)
	inc	hl
	push	hl
	ex	de,hl
	call	packoff
	ld	a,(hl)
	inc	hl
	cp	MAXOBJ+1
	jr	c,ll_o1
	ld	a,MAXOBJ
ll_o1:	ld	(nobj),a
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
	jr	z,ll_o2
	ld	de,OBJS
	ldir
ll_o2:	pop	hl
	call	ll_ptr		; the level's script: items, rules, texts
	ld	(litems),de
	call	ll_ptr
	ld	(lrules),de
	call	ll_ptr
	ld	(ltexts),de
	ld	de,MAZE		; the map, one byte per cell
	ld	bc,32*24
	ldir
	xor	a		; where the script items start
	ld	(nitems),a
	ld	hl,(litems)
	ld	a,h
	or	l
	jr	z,ll_i9
	ld	a,(hl)
	cp	MAXITEMS+1
	jr	c,ll_i1
	ld	a,MAXITEMS
ll_i1:	ld	(nitems),a
	or	a
	jr	z,ll_i9
	ld	b,a
	inc	hl
	ld	de,ILOCS
ll_i2:	ld	a,(hl)
	ld	(de),a
	inc	hl
	inc	de
	ld	a,(hl)
	ld	(de),a
	inc	de
	ld	a,5
	call	addhla
	djnz	ll_i2
ll_i9:	call	itembits
	xor	a
	ld	(KEYS),a
	ld	(KEYS+1),a
	ld	(KEYS+2),a
	ld	(KEYS+3),a
	ld	(hasam),a
	ld	(omode),a
	ld	(inlen),a
	ld	(tcol),a
	ld	hl,0
	ld	(gaddr),hl
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
	call	packoff
	call	msgc
	ld	hl,(lvintro)
	call	packoff
	jp	msg

ll_ptr:				; DE = pack address of the word at HL (0 stays 0); HL += 2
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	a,d
	or	e
	ret	z
	push	hl
	ld	hl,(packp)
	add	hl,de
	ex	de,hl
	pop	hl
	ret

packoff:			; HL = pack start + HL
	ld	de,(packp)
	add	hl,de
	ret

; ===========================================================================
;  Screen helpers
; ===========================================================================
cls:	ld	hl,VIDEO
	ld	(hl),20h
	ld	de,VIDEO+1
	ld	bc,1023
	ldir
	ret

frame:	ld	hl,VIDEO+32	; vertical divider
	ld	de,64
	ld	b,16
fr_1:	ld	(hl),0AAh
	add	hl,de
	djnz	fr_1
	ret

; ---- text window output -------------------------------------------------
; omode = 0: characters go to the scrolling text window
; omode = 1: characters go to (vptr) - used for the status line
txaddr:	ld	a,(trow)	; HL = video address of text cursor
	ld	l,a
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	de,VIDEO+TXTCOL
	add	hl,de
	ld	a,(tcol)
	ld	e,a
	ld	d,0
	add	hl,de
	ret

newline:
	push	af
	push	bc
	push	de
	push	hl
	xor	a
	ld	(tcol),a
	ld	a,(trow)
	cp	TXTBOT
	jr	nc,nl_scr
	inc	a
	ld	(trow),a
	jr	nl_clr
nl_scr:	ld	hl,VIDEO+(TXTTOP+1)*64+TXTCOL
	ld	de,VIDEO+TXTTOP*64+TXTCOL
	ld	a,TXTBOT-TXTTOP
nl_s1:	push	af
	push	hl
	push	de
	ld	bc,TXTW
	ldir
	pop	de
	pop	hl
	ld	bc,64
	add	hl,bc
	ex	de,hl
	add	hl,bc
	ex	de,hl
	pop	af
	dec	a
	jr	nz,nl_s1
nl_clr:	call	txaddr
	ld	b,TXTW
nl_c1:	ld	(hl),20h
	inc	hl
	djnz	nl_c1
	pop	hl
	pop	de
	pop	bc
	pop	af
	ret

putc:	push	af		; print character A
	ld	a,(omode)
	or	a
	jr	nz,putv
	pop	af
	push	hl
	push	de
	push	af
	ld	a,(tcol)
	cp	TXTW
	call	nc,newline
	call	txaddr
	pop	af
	ld	(hl),a
	ld	hl,tcol
	inc	(hl)
	pop	de
	pop	hl
	ret
putv:	pop	af
	push	hl
	ld	hl,(vptr)
	ld	(hl),a
	inc	hl
	ld	(vptr),hl
	pop	hl
	ret

msg:	call	newline		; print string HL on a new line
msgc:	ld	a,(hl)		; continue printing string HL (0 = end, 13 or 1 = newline)
	inc	hl
	or	a
	ret	z
	cp	0Dh
	jr	z,mc_nl
	cp	1
	jr	z,mc_nl
	call	putc
	jr	msgc
mc_nl:	call	newline
	jr	msgc

putdec:				; print HL in decimal, no leading zeros
	push	af
	push	bc
	push	de
	push	hl
	ld	e,0
	ld	bc,-10000
	call	pd_1
	ld	bc,-1000
	call	pd_1
	ld	bc,-100
	call	pd_1
	ld	bc,-10
	call	pd_1
	ld	a,l
	add	a,'0'
	call	putc
	pop	hl
	pop	de
	pop	bc
	pop	af
	ret
pd_1:	ld	a,'0'-1
pd_2:	inc	a
	add	hl,bc
	jr	c,pd_2
	sbc	hl,bc
	cp	'0'
	jr	nz,pd_3
	inc	e
	dec	e
	ret	z
pd_3:	ld	e,1
	jp	putc

putdeca:			; print A in decimal
	push	hl
	ld	l,a
	ld	h,0
	call	putdec
	pop	hl
	ret

; ===========================================================================
;  Maze helpers
; ===========================================================================
caddr:				; E = x, D = y -> HL = cell address, or 0 if outside
	ld	a,e
	cp	32
	jr	nc,ca_0
	ld	a,d
	cp	24
	jr	nc,ca_0
	ld	l,d
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	a,e
	add	a,l
	ld	l,a
	ld	de,MAZE
	add	hl,de
	ret
ca_0:	ld	hl,0
	ret

herecode:			; A = code of player's cell, HL = its address
	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	call	caddr
	ld	a,(hl)
	and	3Fh
	ret

getvec:				; A = direction -> B = dx, C = dy
	and	3
	ld	hl,DXT
	call	addhla
	ld	b,(hl)
	inc	hl
	inc	hl
	inc	hl
	inc	hl
	ld	c,(hl)
	ret

addhla:	add	a,l		; HL += A
	ld	l,a
	adc	a,h
	sub	l
	ld	h,a
	ret

ptrtab:				; HL = word at table HL + 2*A
	add	a,a
	call	addhla
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a
	ret

mul3:	ld	d,a
	add	a,a
	add	a,d
	ret

; view cell table VA: 5 depths x (left, centre, right) addresses
cellv:				; A = view index -> A = code (wall if outside), HL = addr
	ld	hl,VA
	call	ptrtab
	ld	a,h
	or	l
	ld	a,C_WALL
	ret	z
	ld	a,(hl)
	and	3Fh
	ret

markv:				; mark view cell A as seen
	call	cellv
	ld	a,h
	or	l
	ret	z
	set	7,(hl)
	ret

solid:				; Z set if code A looks solid (wall, door, secret wall)
	cp	C_WALL
	ret	z
	cp	C_DOOR
	ret	z
	cp	C_SECRET
	ret

; ===========================================================================
;  3D view renderer
; ===========================================================================
render:				; draw the view from (px,py) facing (dir)
	xor	a
renderp:			; A = phase 0-3: view A/4 of a step further forward
	ld	l,a		; select that phase's segment and sprite tables
	add	a,a
	add	a,a
	add	a,l
	add	a,a		; A * 10
	ld	hl,PHASETAB
	call	addhla
	ld	de,gSEGL
	ld	bc,10
	ldir
	ld	a,(dir)
	call	getvec
	ld	a,b
	ld	(fx),a
	ld	a,c
	ld	(fy),a
	ld	a,(dir)
	add	a,3
	call	getvec
	ld	a,b
	ld	(lfx),a
	ld	a,c
	ld	(lfy),a
; build the table of visible cell addresses
	ld	a,(px)
	ld	(cx),a
	ld	a,(py)
	ld	(cy),a
	ld	ix,VA
	ld	b,5
rv_1:	push	bc
	ld	a,(lfx)		; left
	ld	b,a
	ld	a,(cx)
	add	a,b
	ld	e,a
	ld	a,(lfy)
	ld	b,a
	ld	a,(cy)
	add	a,b
	ld	d,a
	call	caddr
	ld	(ix+0),l
	ld	(ix+1),h
	ld	a,(cx)		; centre
	ld	e,a
	ld	a,(cy)
	ld	d,a
	call	caddr
	ld	(ix+2),l
	ld	(ix+3),h
	ld	a,(lfx)		; right
	ld	b,a
	ld	a,(cx)
	sub	b
	ld	e,a
	ld	a,(lfy)
	ld	b,a
	ld	a,(cy)
	sub	b
	ld	d,a
	call	caddr
	ld	(ix+4),l
	ld	(ix+5),h
	ld	a,(fx)		; step forward
	ld	b,a
	ld	a,(cx)
	add	a,b
	ld	(cx),a
	ld	a,(fy)
	ld	b,a
	ld	a,(cy)
	add	a,b
	ld	(cy),a
	ld	de,6
	add	ix,de
	pop	bc
	djnz	rv_1
	call	kpoll
; find the first solid cell straight ahead (1..4, 5 = none)
	ld	b,1
rf_1:	ld	a,b
	call	mul3
	inc	a
	call	cellv
	call	solid
	jr	z,rf_2
	inc	b
	ld	a,b
	cp	5
	jr	c,rf_1
rf_2:	ld	a,b
	ld	(front),a
	cp	5
	jr	c,rf_3
	ld	a,4
rf_3:	ld	(kmax),a
; mark centre cells 0..kmax as seen
	ld	b,0
rm_1:	ld	a,b
	call	mul3
	inc	a
	call	markv
	inc	b
	ld	a,(kmax)
	cp	b
	jr	nc,rm_1
; side segments for depth 0 .. kmax-1
	ld	b,0
rs_1:	ld	a,(kmax)
	cp	b
	jr	z,rs_done
	ld	a,b
	call	mul3
	ld	(segi),a
	push	bc
	ld	c,0		; left
	call	sidestate
	ld	hl,segi
	add	a,(hl)
	ld	hl,(gSEGL)
	call	ptrtab
	call	blit
	pop	bc
	push	bc
	ld	c,2		; right
	call	sidestate
	ld	hl,segi
	add	a,(hl)
	ld	hl,(gSEGR)
	call	ptrtab
	call	blit
	pop	bc
	inc	b
	jr	rs_1
rs_done:
	call	kpoll
; front face (or blank centre if nothing within range)
	ld	a,(front)
	cp	5
	jr	c,rr_f
	ld	hl,(gF5)
	call	blit
	jr	rr_spr
rr_f:	ld	b,a
	call	mul3
	inc	a
	call	cellv
	ld	c,0
	cp	C_DOOR
	jr	nz,rr_f1
	inc	c
rr_f1:	ld	a,b
	dec	a
	add	a,a
	add	a,c
	ld	hl,(gSEGF)
	call	ptrtab
	call	blit
; nearest object straight ahead, depth 1 .. min(front-1, 3)
rr_spr:	ld	b,1
rr_s1:	ld	a,(front)
	cp	b
	jr	z,rr_copy
	jr	c,rr_copy
	ld	a,b
	cp	4
	jr	nc,rr_copy
	call	mul3
	inc	a
	call	cellv
	or	a
	jr	nz,rr_s2
	bit	6,(hl)		; a script item lying here?
	jr	z,rr_s1n
	ld	a,C_THING
	jr	rr_s3
rr_s1n:	inc	b
	jr	rr_s1
rr_s2:	cp	C_LADDER
	jr	c,rr_copy
	cp	C_TRAP		; a hidden trap looks like bare floor
	jr	nz,rr_s3
	call	getobj
	ld	a,(ix+3)
	or	a
	ld	a,C_TRAP
	jr	nz,rr_s3
	inc	b
	jr	rr_s1
rr_s3:	call	mul3		; index = code*3 + depth-1
	add	a,b
	dec	a
	ld	hl,(gSPR)
	call	ptrtab
	ld	a,h
	or	l
	call	nz,sprite
; copy the finished view to the screen (unless noshow is set)
rr_copy:
	call	kpoll
	ld	a,(noshow)
	or	a
	ret	nz
showbuf:
	ld	hl,VBUF
	ld	de,VIDEO
	ld	b,16
rc_1:	push	bc
	ld	bc,32
	ldir
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	rc_1
	ret

sidestate:			; B = depth, C = 0 left / 2 right -> A = segment state
	ld	a,b		; 2 = wall, 1 = opening with wall face, 0 = opening
	call	mul3
	add	a,c
	push	af
	call	markv
	pop	af
	call	cellv
	call	solid
	ld	a,2
	ret	z
	ld	a,b
	inc	a
	call	mul3
	add	a,c
	push	af
	call	markv
	pop	af
	call	cellv
	call	solid
	ld	a,1
	ret	z
	xor	a
	ret

blit:				; copy segment HL (col, width, 16 rows) into VBUF
	ld	e,(hl)		; width + 80H means blank: fill instead of copy
	inc	hl
	ld	c,(hl)
	inc	hl
	push	hl
	ld	hl,VBUF
	ld	d,0
	add	hl,de
	ex	de,hl
	pop	hl
	bit	7,c
	jr	nz,bl_fill
	ld	b,16
bl_1:	push	bc
	push	de
	ld	b,0
	ldir
	pop	de
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	bl_1
	ret
bl_fill:
	res	7,c
	ex	de,hl		; HL = destination
	ld	b,16
bl_f1:	push	bc
	push	hl
	ld	b,c
bl_f2:	ld	(hl),80h
	inc	hl
	djnz	bl_f2
	pop	hl
	ld	de,32
	add	hl,de
	pop	bc
	djnz	bl_f1
	ret

; ---- animation ------------------------------------------------------------
fdelay:				; pause between animation frames, queueing any keys
	ld	b,16
fd_1:	push	bc
	call	inkey
	or	a
	call	nz,kpush
	ld	bc,FRAMEDLY
fd_2:	dec	bc
	ld	a,b
	or	c
	jr	nz,fd_2
	pop	bc
	djnz	fd_1
	ret

kpoll:				; queue a key if one has just been pressed. The ROM routine
	call	inkey		; debounces and drops a key already released, so busy
	or	a		; code polls often to catch short taps early
	ret	z
kpush:				; add key A to the type-ahead queue (16 keys)
	push	hl
	ld	l,a
	ld	a,(kqn)
	cp	16
	jr	nc,kp_1
	ld	h,a
	ld	a,l
	ld	l,h
	ld	h,0
	push	de
	ld	de,KQ
	add	hl,de
	pop	de
	ld	(hl),a
	ld	hl,kqn
	inc	(hl)
kp_1:	pop	hl
	ret

animstep:			; show the view at phase A, then pause
	call	renderp
	jr	fdelay

turn:				; A = 0 turn right, 1 turn left, sliding the new view in
	ld	(tdir),a
	ld	hl,VBUF		; VBUF still holds the view on screen
	ld	de,OLDBUF
	ld	bc,32*16
	ldir
	ld	a,(tdir)
	or	a
	ld	a,(dir)
	jr	nz,tu_l
	inc	a
	jr	tu_s
tu_l:	add	a,3
tu_s:	and	3
	ld	(dir),a
	ld	a,1		; draw the new view off screen
	ld	(noshow),a
	call	render
	xor	a
	ld	(noshow),a
	ld	a,8		; three frames, sliding 8, 16, 24 columns
tu_f:	ld	(tshift),a
	call	slide
	call	fdelay
	ld	a,(tshift)
	add	a,8
	cp	32
	jr	c,tu_f
	jp	showbuf

slide:				; one turning frame: old view moves out, new view moves in
	ld	hl,0
	ld	(toff),hl
	ld	de,VIDEO
	ld	c,16
sl_row:	push	bc
	push	de
	ld	a,(tdir)
	or	a
	jr	nz,sl_left
	ld	hl,(toff)	; turning right: old[s..31] then new[0..s-1]
	ld	bc,OLDBUF
	add	hl,bc
	ld	a,(tshift)
	call	addhla
	call	sl_rest
	ld	hl,(toff)
	ld	bc,VBUF
	add	hl,bc
	call	sl_part
	jr	sl_next
sl_left:			; turning left: new[32-s..31] then old[0..31-s]
	ld	hl,(toff)
	ld	bc,VBUF
	add	hl,bc
	ld	a,(tshift)
	neg
	add	a,32
	call	addhla
	call	sl_part
	ld	hl,(toff)
	ld	bc,OLDBUF
	add	hl,bc
	call	sl_rest
sl_next:
	ld	hl,(toff)
	ld	bc,32
	add	hl,bc
	ld	(toff),hl
	pop	de
	ld	hl,64
	add	hl,de
	ex	de,hl
	pop	bc
	dec	c
	jr	nz,sl_row
	ret
sl_part:			; copy (tshift) bytes HL -> DE
	ld	a,(tshift)
	jr	sl_cp
sl_rest:			; copy 32-(tshift) bytes HL -> DE
	ld	a,(tshift)
	neg
	add	a,32
sl_cp:	ld	c,a
	ld	b,0
	ldir
	ret

sprite:				; mask sprite HL (col,row,w,h, mask/bits pairs) into VBUF
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	c,(hl)
	inc	hl
	ld	b,(hl)
	inc	hl
	push	hl
	ld	l,d
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	d,0
	add	hl,de
	ld	de,VBUF
	add	hl,de
	ex	de,hl
	pop	hl
sp_1:	push	bc
	push	de
sp_2:	ld	a,(hl)		; mask
	cpl
	ex	de,hl
	and	(hl)
	ex	de,hl
	inc	hl
	or	(hl)		; bits
	or	80h
	ld	(de),a
	inc	hl
	inc	de
	dec	c
	jr	nz,sp_2
	pop	de
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	sp_1
	ret

; ===========================================================================
;  Map window: 15 x 7 cells around the player, 2 characters per cell
; ===========================================================================
drawmap:
	ld	a,(py)
	sub	3
	ld	(my),a
	ld	hl,VIDEO+MAPCOL
	ld	(vptr),hl
	ld	c,0
dm_row:	ld	a,(px)
	sub	7
	ld	(mx),a
	ld	de,(vptr)
	ld	b,15
dm_col:	ld	a,c
	cp	3
	jr	nz,dm_n
	ld	a,b
	cp	8
	jr	nz,dm_n
	ld	a,(dir)		; the player, drawn as a pair of arrows
	ld	hl,ARROWS
	call	addhla
	ld	a,(hl)
	ld	(de),a
	inc	de
	ld	(de),a
	inc	de
	jr	dm_next
dm_n:	ld	a,(mx)
	cp	32
	jr	nc,dm_blank
	ld	a,(my)
	cp	24
	jr	nc,dm_blank
	push	bc
	push	de
	ld	e,a		; (my)
	ld	d,a
	ld	a,(mx)
	ld	e,a
	call	caddr
	pop	de
	pop	bc
	ld	a,(hl)
	bit	7,a
	jr	z,dm_blank
	and	3Fh
	jr	nz,dm_x		; bare floor with a script item: +
	bit	6,(hl)
	ld	a,0
	jr	z,dm_x
	ld	a,C_THING
	jr	dm_t
dm_x:
	cp	C_TRAP		; hidden traps stay hidden on the map too
	jr	nz,dm_t
	push	bc
	push	de
	call	getobj
	pop	de
	pop	bc
	ld	a,(ix+3)
	or	a
	ld	a,C_TRAP
	jr	nz,dm_t
	xor	a
dm_t:	add	a,a
	ld	hl,MAPCH
	call	addhla
	ld	a,(hl)
	ld	(de),a
	inc	de
	inc	hl
	ld	a,(hl)
	ld	(de),a
	inc	de
	jr	dm_next
dm_blank:
	ld	a,20h
	ld	(de),a
	inc	de
	ld	(de),a
	inc	de
dm_next:
	ld	hl,mx
	inc	(hl)
	djnz	dm_col
	push	bc
	call	kpoll		; don't miss key taps while drawing
	pop	bc
	ld	hl,(vptr)
	ld	de,64
	add	hl,de
	ld	(vptr),hl
	ld	hl,my
	inc	(hl)
	inc	c
	ld	a,c
	cp	7
	jp	c,dm_row
	ret

; ===========================================================================
;  Status line (row 7 of the right-hand side)
; ===========================================================================
status:	ld	hl,VIDEO+STROW*64+33
	ld	b,31
st_1:	ld	(hl),8Ch
	inc	hl
	djnz	st_1
	ld	a,1
	ld	(omode),a
	ld	hl,VIDEO+STROW*64+34
	ld	(vptr),hl
	ld	a,'L'
	call	putc
	ld	a,(level)
	inc	a
	call	putdeca
	ld	hl,S_HP
	call	msgc
	ld	a,(hp)
	call	putdeca
	ld	hl,S_GOLD
	call	msgc
	ld	hl,(gold)
	call	putdec
	ld	hl,S_POT
	call	msgc
	ld	a,(potions)
	call	putdeca
	ld	a,' '
	call	putc
	ld	a,(KEYS)
	ld	hl,KEYS+1
	or	(hl)
	inc	hl
	or	(hl)
	inc	hl
	or	(hl)
	ld	c,'K'
	call	st_flag
	ld	a,(hassw)
	ld	c,'S'
	call	st_flag
	ld	a,(hasam)
	ld	c,'A'
	call	st_flag
	ld	a,' '
	call	putc
	ld	a,(dir)
	ld	hl,DIRL
	call	addhla
	ld	a,(hl)
	call	putc
	ld	a,' '
	call	putc
	xor	a
	ld	(omode),a
	ret
st_flag:
	or	a
	ld	a,c
	jr	nz,st_f1
	ld	a,'-'
st_f1:	jp	putc

; ===========================================================================
;  Command input (row 15).  Arrow keys act at once when the line is empty;
;  otherwise letters build a command that ENTER executes.
; ===========================================================================
command:
	xor	a
	ld	(inlen),a
cm_0:	call	drawin
cm_1:	call	waitkey
	cp	0Dh
	jp	z,cm_ent
	cp	08h
	jr	z,cm_lt
	cp	09h
	jr	z,cm_rt
	cp	5Bh
	jr	z,cm_up
	cp	0Ah
	jr	z,cm_dn
	cp	1Fh
	jr	z,cm_clr
	cp	18h
	jr	z,cm_clr
	cp	'a'
	jr	c,cm_2
	cp	'z'+1
	jr	nc,cm_1
	sub	20h
cm_2:	cp	20h
	jr	c,cm_1
	cp	5Bh
	jr	nc,cm_1
	ld	b,a
	ld	a,(inlen)
	cp	26
	jr	nc,cm_1
	ld	hl,INBUF
	call	addhla
	ld	(hl),b
	ld	hl,inlen
	inc	(hl)
	jr	cm_0
cm_lt:	ld	a,(inlen)
	or	a
	jr	nz,cm_bs
	call	act_left
	jp	gturn
cm_bs:	dec	a
	ld	(inlen),a
	jr	cm_0
cm_rt:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_right
	jp	gturn
cm_up:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_fwd
	jp	gturn
cm_dn:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_back
	jp	gturn
cm_clr:	xor	a
	ld	(inlen),a
	jp	cm_0
cm_ent:	ld	a,(inlen)
	or	a
	jp	z,cm_1
	ld	hl,INBUF
	call	addhla
	ld	(hl),0
	ld	hl,M_ECHO
	call	msg
	ld	hl,INBUF
	call	msgc
	xor	a
	ld	(inlen),a
	call	drawin
	jp	exec

drawin:	ld	hl,VIDEO+15*64+TXTCOL
	ld	(hl),'>'
	inc	hl
	ld	(hl),' '
	inc	hl
	ld	de,INBUF
	ld	a,(inlen)
	or	a
	jr	z,di_2
	ld	b,a
di_1:	ld	a,(de)
	ld	(hl),a
	inc	hl
	inc	de
	djnz	di_1
di_2:	ld	(hl),0B0h	; cursor
	inc	hl
di_3:	ld	a,h
	cp	40h
	ret	nc
	ld	(hl),20h
	inc	hl
	jr	di_3

; ---- parser ---------------------------------------------------------------
skipsp:	ld	a,(hl)
	cp	' '
	ret	nz
	inc	hl
	jr	skipsp

exec:	ld	hl,INBUF
	call	skipsp
	ld	a,(hl)
	or	a
	ret	z
	ld	(wordp),hl
ex_1:	ld	a,(hl)		; find end of verb
	or	a
	jr	z,ex_3
	cp	' '
	jr	z,ex_2
	inc	hl
	jr	ex_1
ex_2:	ld	(hl),0
	inc	hl
	call	skipsp
ex_3:	ld	(nounp),hl
	ld	c,2		; the noun, if the script knows it
	call	findword
	ld	(nounid),a
	ld	de,VOCAB	; the verb: built-in first
ex_4:	ld	a,(de)
	or	a
	jr	z,ex_unk
	ld	hl,(wordp)
ex_5:	ld	a,(de)
	cp	(hl)
	jr	nz,ex_6
	or	a
	jr	z,ex_hit
	inc	de
	inc	hl
	jr	ex_5
ex_6:	ld	a,(de)		; skip rest of this entry
	inc	de
	or	a
	jr	nz,ex_6
	inc	de
	inc	de
	inc	de
	inc	de
	jr	ex_4
ex_hit:	inc	de
	ld	a,(de)		; turn flag
	ld	(tflag),a
	inc	de
	ld	a,(de)		; verb id
	ld	(verbid),a
	inc	de
	ld	a,(de)
	ld	l,a
	inc	de
	ld	a,(de)
	ld	h,a
	ld	(vhandler),hl
	jr	ex_rules
ex_unk:	ld	hl,(wordp)	; then the script's own verbs
	ld	c,1
	call	findword
	or	a
	jr	z,ex_bad
	ld	(verbid),a
	cp	32
	jr	nc,ex_cust
	call	bverb		; a script synonym of a built-in verb
	jr	ex_rules
ex_cust:
	ld	a,1
	ld	(tflag),a
	ld	hl,0
	ld	(vhandler),hl
ex_rules:
	call	runverb		; the level's rules get the first say
	cp	2
	jp	z,gturn
	cp	3
	jr	z,ex_do
	ld	hl,(vhandler)
	ld	a,h
	or	l
	jr	nz,ex_do
	ld	a,(rres)	; a script verb that did nothing
	or	a
	ld	hl,M_UNK
	jp	z,msg
	ld	hl,M_CANT
	jp	msg
ex_do:	ld	hl,(vhandler)
	ld	a,h
	or	l
	jp	z,gturn
	ld	de,ex_ret
	push	de
	jp	(hl)
ex_ret:	ld	a,(tflag)
	or	a
	ret	z
	jp	gturn
ex_bad:	ld	hl,M_UNK
	jp	msg

; ===========================================================================
;  Actions
; ===========================================================================
act_left:
	ld	a,1
	jp	turn
act_right:
	xor	a
	jp	turn
act_around:
	call	act_right
	jr	act_right
act_turn:
	ld	hl,(nounp)
	ld	a,(hl)
	cp	'L'
	jr	z,act_left
	cp	'R'
	jr	z,act_right
	jr	act_around

act_fwd:
	xor	a
	ld	(mvback),a
	ld	a,(fx)
	ld	b,a
	ld	a,(fy)
	ld	c,a
	jr	domove
act_back:
	ld	a,1
	ld	(mvback),a
	ld	a,(fx)
	neg
	ld	b,a
	ld	a,(fy)
	neg
	ld	c,a
domove:	ld	a,(px)
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
	jr	z,mv_wall
	ld	a,(hl)
	and	3Fh
	cp	C_WALL
	jr	z,mv_wall
	cp	C_DOOR
	jr	z,mv_door
	cp	C_GUARD
	jr	z,mv_grd
	cp	C_NPC		; bumping into someone: talk
	jp	nc,talkat
	push	hl		; the move is possible: animate it
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
	jp	z,arrive
	ld	a,3		; backward: the same frames seen from the new cell
	call	animstep
	ld	a,2
	call	animstep
	ld	a,1
	call	animstep
	jp	arrive
mv_wall:
	ld	hl,M_BUMP
	jp	msg
mv_door:
	ld	hl,M_DOOR
	jp	msg
mv_grd:	ld	hl,M_BARS
	jp	msg

; ---- objects: per-cell parameter records ----------------------------------
; Each level carries a list of 8-byte records, copied into OBJS on loading:
;   x, y, a, b, c, d, text (word offset into the pack, 0 = none)
; Meaning of a..d by cell type (0 always means "use the default"):
;   gold      a = amount                 potion    a = heal
;   key       a = colour 0-3             door      a = colour of key needed
;   guardian  a = hit points (counts down), b = max damage, c = attack chance 0-255
;   sign      text                       trap      a = damage, b = visible, c = one-shot
;   teleport  a,b = destination x,y      fountain  a = heal (0 = full), b = uses (0 = endless)
;   plate     a,b = cell to open, d = already used
;   secret wall: text shown when found.  Text on a guardian is shown by LOOK.
objat:				; HL = cell address -> HL = its record, NZ; Z if none
	push	bc
	push	de
	ld	de,MAZE
	or	a
	sbc	hl,de
	ld	a,l
	and	31
	ld	c,a		; x
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	b,h		; y
	ld	a,(nobj)
	or	a
	jr	z,oa_none
	ld	e,a
	ld	hl,OBJS
oa_1:	ld	a,(hl)
	cp	c
	jr	nz,oa_2
	inc	hl
	ld	a,(hl)
	dec	hl
	cp	b
	jr	z,oa_hit
oa_2:	ld	a,8
	call	addhla
	dec	e
	jr	nz,oa_1
oa_none:
	ld	hl,0
	xor	a
	pop	de
	pop	bc
	ret
oa_hit:	ld	a,1
	or	a
	pop	de
	pop	bc
	ret

getobj:				; HL = cell address -> IX = record (or all-zero defaults)
	call	objat		; hasobj = 1 if the cell has its own record
	ld	(hasobj),a
	jr	z,go_1
	push	hl
	pop	ix
	ret
go_1:	ld	ix,DEFOBJ
	ret

objtext:			; HL = text of record IX, NZ; Z if it has none
	ld	l,(ix+6)
	ld	h,(ix+7)
	ld	a,h
	or	l
	ret	z
	jp	packoff		; (16-bit add leaves Z clear)

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

; ---- arriving on a cell -------------------------------------------------------
arrive0:	xor	a
	ld	(teleflag),a
arrive2:
	call	herecode
	push	af
	call	getobj
	pop	af
	cp	C_LADDER
	jr	z,ar_lad
	cp	C_SIGN
	jr	nc,ar_space
	cp	C_KEY
	ret	c
ar_item:			; "YOU SEE <item>."
	call	itemname
	push	hl
	ld	hl,M_YOUSEE
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	jp	putc
ar_lad:	ld	a,(hasam)
	or	a
	jp	nz,win
	ld	hl,M_LADDER
	jp	msg
ar_space:
	cp	C_SIGN
	jr	z,ar_sign
	cp	C_TRAP
	jr	z,ar_trap
	cp	C_TELE
	jp	z,ar_tele
	cp	C_FOUNT
	jp	z,ar_fount
	cp	C_PLATE
	jp	z,ar_plate
				; secret wall: it was never there
	call	herecode
	call	floorcell
	ld	de,M_SECRET
	jr	ar_say
ar_sign:
	ld	hl,M_SIGNHD
	call	msg
	call	objtext
	jp	nz,msg
	ld	hl,M_SIGN0
	jp	msg
ar_say:				; show the record's text, else message DE
	call	objtext
	jp	nz,msg
	ex	de,hl
	jp	msg
ar_trap:
	ld	a,(hasobj)
	or	a
	jr	z,at_t1
	ld	(ix+3),1	; it is visible from now on
	ld	a,(ix+4)
	or	a
	jr	z,at_t1
	call	herecode	; one-shot: the trap is spent
	call	floorcell
at_t1:	ld	de,M_TRAP
	call	ar_say
	ld	a,(ix+2)
	ld	c,3
	call	orelse
	ld	b,a
	jp	hurt
ar_tele:
	ld	a,(teleflag)	; no chains of teleporters
	or	a
	ret	nz
	ld	e,(ix+2)
	ld	d,(ix+3)
	push	de
	call	caddr
	pop	de
	ld	a,h
	or	l
	ret	z
	ld	a,(hl)
	and	3Fh
	cp	C_WALL
	ret	z
	cp	C_DOOR
	ret	z
	cp	C_GUARD
	ret	z
	cp	C_NPC
	ret	nc
	set	7,(hl)
	ld	a,e
	ld	(px),a
	ld	a,d
	ld	(py),a
	ld	de,M_TELE
	call	ar_say
	ld	a,1
	ld	(teleflag),a
	jp	arrive2		; see what is at the destination
ar_fount:
	ld	a,(maxhp)
	ld	c,a
	ld	a,(ix+2)	; 0 = heal fully
	call	orelse
	ld	b,a
	call	heal_by
	ld	de,M_FOUNT
	call	ar_say
	ld	hl,M_HPIS
	call	msg
	ld	a,(hp)
	call	putdeca
	ld	a,(hasobj)
	or	a
	ret	z
	ld	a,(ix+3)	; limited uses?
	or	a
	ret	z
	dec	a
	ld	(ix+3),a
	ret	nz
	call	herecode
	call	floorcell
	ld	hl,M_DRY
	jp	msg
ar_plate:
	ld	a,(hasobj)
	or	a
	ret	z
	ld	a,(ix+5)
	or	a
	ret	nz
	ld	(ix+5),1
	ld	e,(ix+2)
	ld	d,(ix+3)
	call	caddr
	ld	a,h
	or	l
	ret	z
	ld	a,(hl)		; the target becomes open floor
	and	0C0h
	ld	(hl),a
	ld	de,M_PLATE
	jp	ar_say

itemname:			; A = item code, IX = its record -> HL = name
	cp	C_KEY
	jr	nz,in_1
	ld	a,(ix+2)
keyname:			; A = key colour -> HL = name
	and	3
	jr	in_2
in_1:	sub	2		; sword, potion, gold, amulet = names 4-7
in_2:	add	a,a
	add	a,PK_NAMES
	jp	packw

act_take:
	ld	a,(nounid)	; TAKE <script noun>
	or	a
	jr	z,tk_plain
	ld	c,2
	call	finditem
	cp	0FFh
	jp	nz,take_item
	ld	hl,M_NOSEE
	jp	msg
tk_plain:
	call	herecode
	cp	C_KEY
	jp	c,tk_none
	cp	C_SIGN
	jp	nc,tk_none
	push	af
	push	hl
	call	getobj
	pop	hl
	call	floorcell	; cell becomes (seen) floor
	pop	af
	cp	C_GOLD
	jr	z,tk_gold
	push	af
	call	itemname
	push	hl
	ld	hl,M_TAKE
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	call	putc
	pop	af
	cp	C_KEY
	jr	nz,tk_1
	ld	a,(ix+2)	; one more key of this colour
	and	3
	ld	hl,KEYS
	call	addhla
	inc	(hl)
	ret
tk_1:	cp	C_SWORD
	jr	nz,tk_2
	ld	(hassw),a
	ret
tk_2:	cp	C_POTION
	jr	nz,tk_3
	ld	a,(potions)	; remember how strong this potion is
	cp	16
	ret	nc
	ld	hl,PHEAL
	call	addhla
	ld	a,(heal)
	ld	c,a
	ld	a,(ix+2)
	call	orelse
	ld	(hl),a
	ld	hl,potions
	inc	(hl)
	ret
tk_3:	ld	(hasam),a	; the amulet
	ld	hl,M_GOTAM
	jp	msg
tk_gold:
	ld	a,(ix+2)	; a fixed amount, or a random one
	or	a
	jr	nz,tg_2
	call	rnd
	ld	a,(goldrng)
	ld	b,a
	ld	a,l
	and	7Fh
	call	modb
	ld	hl,goldmin
	add	a,(hl)
tg_2:	ld	e,a
	ld	d,0
	ld	hl,(gold)
	add	hl,de
	ld	(gold),hl
	ld	hl,M_GOLD1
	call	msg
	ld	a,e
	call	putdeca
	ld	hl,M_GOLD2
	jp	msgc
tk_none:
	call	firsthere	; nothing built in: a script item?
	cp	0FFh
	jp	nz,take_item
	ld	hl,M_NOTAKE
	jp	msg

act_use:
	ld	hl,(nounp)
	ld	a,(hl)
	cp	'P'
	jr	z,act_drink
act_open:
	ld	a,4		; cell straight ahead
	call	cellv
	cp	C_DOOR
	jr	nz,op_none
	push	hl
	call	getobj
	pop	hl
	ld	a,(ix+2)	; needs a key of the door's colour
	and	3
	ld	de,KEYS
	add	a,e
	ld	e,a
	adc	a,d
	sub	e
	ld	d,a
	ld	a,(de)
	or	a
	jr	z,op_lock
	dec	a
	ld	(de),a
	call	floorcell
	ld	hl,M_OPENED
	jp	msg
op_lock:
	ld	hl,M_NEED
	call	msg
	ld	a,(ix+2)
	call	keyname
	call	msgc
	ld	a,'.'
	jp	putc
op_none:
	ld	hl,M_NOOPEN
	jp	msg

act_drink:
	ld	a,(potions)
	or	a
	jr	z,dr_none
	dec	a
	ld	(potions),a
	ld	hl,PHEAL
	call	addhla
	ld	b,(hl)
	call	heal_by
	ld	hl,M_DRINK
	call	msg
	ld	a,(hp)
	jp	putdeca
dr_none:
	ld	hl,M_NOPOT
	jp	msg

act_attack:
	ld	a,4
	call	cellv
	cp	C_GUARD
	jp	nz,at_air
	ld	a,(hassw)
	or	a
	jp	z,at_fist
	push	hl
	call	getobj
	pop	hl
	call	rnd
	ld	a,(hitch)
	ld	b,a
	ld	a,l
	cp	b
	jr	nc,at_miss
	ld	a,(hasobj)
	or	a
	jr	z,at_old
	ld	a,(ix+2)	; this guardian's own hit points
	or	a
	jr	nz,at_1
	ld	a,(ghpmax)
at_1:	dec	a
	ld	(ix+2),a
	jr	z,at_kill
	jr	at_hit
at_old:	ld	a,4		; no record: level default, one guardian at a time
	call	cellv
	ld	de,(gaddr)
	or	a
	sbc	hl,de
	add	hl,de
	jr	z,at_2
	ld	(gaddr),hl
	ld	a,(ghpmax)
	ld	(ghp),a
at_2:	ld	hl,ghp
	dec	(hl)
	jr	z,at_kill
at_hit:	ld	hl,M_HIT
	jp	msg
at_kill:
	ld	a,4
	call	cellv
	call	floorcell
	ld	hl,0
	ld	(gaddr),hl
	ld	hl,M_KILL
	jp	msg
at_miss:
	ld	hl,M_MISS
	jp	msg
at_fist:
	ld	hl,M_FIST
	jp	msg
at_air:	ld	hl,M_AIR
	jp	msg

act_look:
	call	look_item	; LOOK/EXAMINE <script item>
	ret	z
	ld	hl,M_FACE
	call	msg
	ld	a,(dir)
	ld	hl,DIRNAME
	call	ptrtab
	call	msgc
	ld	a,'.'
	call	putc
	ld	a,4
	call	cellv
	push	af
	call	getobj
	pop	af
	cp	C_NPC		; someone standing there
	jr	c,lk_0
	sub	C_NPC-8
	call	in_2
	push	hl
	ld	hl,M_ASTAND
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	call	putc
	jp	lk_here
lk_0:	ld	hl,M_LWALL
	cp	C_WALL
	jr	z,lk_msg
	cp	C_SECRET	; looks just like a wall
	jr	z,lk_msg
	cp	C_DOOR
	jr	z,lk_door
	cp	C_GUARD
	jr	z,lk_grd
	ld	hl,M_LLAD
	cp	C_LADDER
	jr	z,lk_msg
	ld	hl,M_LPASS
	cp	C_TRAP
	jr	nz,lk_1
	ld	b,a
	ld	a,(ix+3)	; hidden traps look like floor
	or	a
	ld	a,b
	jr	z,lk_msg
lk_1:	cp	C_SIGN
	jr	c,lk_2
	sub	C_SIGN		; other spaces: a fixed description
	ld	hl,LKSPACE
	call	ptrtab
	jr	lk_msg
lk_2:	cp	C_KEY
	jr	c,lk_msg
	call	itemname
	push	hl
	ld	hl,M_AHEAD
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	call	putc
	jr	lk_here
lk_door:
	ld	hl,M_LDOOR
	call	msg
	ld	hl,M_DNEED
	call	msg
	ld	a,(ix+2)
	call	keyname
	call	msgc
	ld	a,'.'
	call	putc
	jr	lk_here
lk_grd:	ld	de,M_LGRD	; the guardian's own description, if it has one
	call	ar_say
	jr	lk_here
lk_msg:	call	msg
lk_here:			; and what is underfoot (without setting anything off)
	ld	c,2
	call	listitems
	call	herecode
	push	af
	call	getobj
	pop	af
	cp	C_SIGN
	jp	z,ar_sign
	cp	C_KEY
	ret	c
	cp	C_SIGN
	ret	nc
	jp	ar_item

act_inv:
	ld	hl,M_CARRY
	call	msg
	ld	c,0		; keys of each colour
iv_k:	ld	a,c
	ld	hl,KEYS
	call	addhla
	ld	a,(hl)
	or	a
	jr	z,iv_k2
	call	newline
	call	putdeca
	ld	hl,M_TIMES
	call	msgc
	ld	a,c
	push	bc
	call	keyname
	call	msgc
	pop	bc
iv_k2:	inc	c
	ld	a,c
	cp	4
	jr	c,iv_k
	ld	a,(hassw)
	or	a
	ld	a,C_SWORD
	call	nz,inv_1
	ld	a,(hasam)
	or	a
	ld	a,C_AMULET
	call	nz,inv_1
	ld	a,(potions)
	or	a
	jr	z,iv_2
	call	newline
	call	putdeca
	ld	hl,M_POTS
	call	msgc
iv_2:	ld	c,1		; script items carried
	call	listitems
	call	newline
	ld	hl,(gold)
	call	putdec
	ld	hl,M_GP
	jp	msgc
inv_1:	call	itemname
	jp	msg

; ---- non-player characters --------------------------------------------------
; Cell codes 16-18 are characters (hermit, merchant, ghost). They block the way;
; walking into one, or TALK, starts a conversation. Their record holds:
;   b = what they do: 0 talk, 1 give an item, 2 sell an item for c gold,
;       3 heal c HP (0 = fully), 4 step aside (vanish) after talking
;   c = gold price, gold given or HP healed
;   d = item given: 1-4 key of colour 0-3, 5 sword, 6 potion, 7 gold, 8 amulet
;       (bit 7 is set once the deed is done)
;   text = first speech, then (after its 0) the speech once the deed is done
act_talk:
	ld	a,4		; the cell straight ahead
	call	cellv
	cp	C_NPC
	jp	nc,talkat
	ld	hl,M_NOONE
	jp	msg

talkat:				; HL = character's cell, A = its code
	ld	(npccell),hl
	sub	C_NPC
	add	a,8		; names 8-10 are the characters
	push	af
	call	getobj
	pop	af
	call	in_2		; "<NAME> SAYS:"
	call	msg
	ld	hl,M_SAYS
	call	msgc
	call	objtext		; first speech (or a greeting)
	jr	nz,tk_t1
	ld	hl,M_NPC0
	ld	(sp1),hl
	ld	(sp2),hl
	jr	tk_t4
tk_t1:	ld	(sp1),hl
	push	hl		; second speech follows the first one's 0
tk_t2:	ld	a,(hl)
	inc	hl
	or	a
	jr	nz,tk_t2
	ld	a,(hl)
	or	a
	jr	nz,tk_t3
	ld	hl,(sp1)	; none: say the first one again
tk_t3:	ld	(sp2),hl
	pop	hl
tk_t4:
	ld	a,1
	ld	(keepview),a	; leave the bubble up until the next action
	ld	a,(ix+5)
	bit	7,a
	jr	z,tk_new
	ld	hl,(sp2)	; deed already done
	jp	speak
tk_new:	push	ix		; (the renderer uses IX)
	call	speak
	pop	ix
	ld	a,(ix+3)	; what does this character do?
	or	a
	ret	z
	cp	2
	jr	z,tk_sell
	cp	3
	jr	z,tk_heal
	cp	4
	jr	z,tk_leave
	call	npcgive		; 1: give
	jr	tk_done
tk_sell:
	ld	hl,(gold)
	ld	e,(ix+4)
	ld	d,0
	or	a
	sbc	hl,de
	jr	c,tk_poor
	ld	(gold),hl
	ld	hl,M_PAY
	call	msg
	ld	a,(ix+4)
	call	putdeca
	ld	hl,M_GOLDD
	call	msgc
	call	npcgive
	jr	tk_done
tk_poor:
	ld	hl,M_NEEDG
	call	msg
	ld	a,(ix+4)
	call	putdeca
	ld	hl,M_GOLDD
	jp	msgc
tk_heal:
	ld	a,(maxhp)
	ld	c,a
	ld	a,(ix+4)
	call	orelse
	ld	b,a
	call	heal_by
	ld	hl,M_HPIS
	call	msg
	ld	a,(hp)
	call	putdeca
	jr	tk_done
tk_leave:
	ld	hl,(npccell)	; the character steps aside: the cell is free
	call	floorcell
	ld	hl,M_VANISH
	call	msg
tk_done:
	ld	a,(hasobj)	; remember the deed is done
	or	a
	ret	z
	set	7,(ix+5)
	ret

npcgive:			; hand over the item in d (IX record)
	ld	a,(ix+5)
	and	7Fh
	ret	z
	cp	5
	jr	nc,ng_1
	dec	a		; a key of colour 0-3
	push	af
	ld	hl,KEYS
	call	addhla
	inc	(hl)
	pop	af
	call	keyname
	jr	ng_say
ng_1:	jr	nz,ng_2
	ld	(hassw),a	; 5: sword
	ld	a,4
	jr	ng_name
ng_2:	cp	6
	jr	nz,ng_3
	ld	a,(potions)	; 6: potion of the usual strength
	cp	16
	jr	nc,ng_2a
	ld	hl,PHEAL
	call	addhla
	ld	a,(heal)
	ld	(hl),a
	ld	hl,potions
	inc	(hl)
ng_2a:	ld	a,5
	jr	ng_name
ng_3:	cp	7
	jr	nz,ng_4
	ld	e,(ix+4)	; 7: gold, c pieces
	ld	d,0
	ld	hl,(gold)
	add	hl,de
	ld	(gold),hl
	ld	a,6
	jr	ng_name
ng_4:	ld	a,1		; 8: the amulet
	ld	(hasam),a
	ld	a,7
	call	in_2
	call	ng_say
	ld	hl,M_GOTAM
	jp	msg
ng_name:
	call	in_2
ng_say:	push	hl
	ld	hl,M_RECV
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	jp	putc

; ---- speech over the 3D view ----------------------------------------------------
; A character's speech is a list of short sentences separated by 01, each one
; pre-wrapped by ZorEdit into lines of up to 28 characters (separated by 13).
; Each sentence appears on its own, with no box around it. Word by word, each
; word comes out of the speaker's mouth and drifts up into its place in the
; sentence above their head. A finished sentence stays up for a moment, then
; the next one replaces it. Any key hurries things along. The last sentence
; stays until the next action.
MOUTHROW equ	8		; where words start: the mouth of a character
MOUTHCOL equ	16		;   standing right in front of the player
FLYDLY	equ	3		; pause per step of a flying word (units of about 10 ms)
WORDDLY	equ	2		; pause after each word lands
SENTDLY	equ	160		; how long a finished sentence stays up

speak:				; HL = speech
	push	hl
	call	msg		; the whole speech also goes in the text window
	pop	hl
sk_next:
	ld	(sptxt),hl
	xor	a
	ld	(skfast),a
	ld	a,1		; a fresh view, off screen
	ld	(noshow),a
	call	render
	xor	a
	ld	(noshow),a
	call	showbuf
	ld	hl,VBUF
	ld	(skrow),hl
	ld	hl,(sptxt)
sk_line:			; HL = start of a line: how long is it?
	push	hl
	ld	b,0
sk_len:	ld	a,(hl)
	or	a
	jr	z,sk_l2
	cp	0Dh
	jr	z,sk_l2
	cp	1
	jr	z,sk_l2
	inc	b
	inc	hl
	jr	sk_len
sk_l2:	ld	a,28		; centre it: column 2 + (28 - length) / 2
	sub	b
	srl	a
	add	a,2
	ld	c,a
	ld	hl,(skrow)	; clear a strip one wider each side
	dec	a
	call	addhla
	ld	a,b
	add	a,2
	ld	e,a
sk_clr:	ld	(hl),20h
	inc	hl
	dec	e
	jr	nz,sk_clr
	ld	hl,(skrow)
	ld	a,c
	call	addhla
	ex	de,hl		; DE = where the words go
	pop	hl
sk_ch:	ld	a,(hl)
	or	a
	jr	z,sk_end
	cp	1
	jr	z,sk_end
	cp	0Dh
	jr	z,sk_eol
	cp	' '
	jr	nz,sk_wd
	ld	(de),a		; spaces just take their place
	inc	de
	inc	hl
	jr	sk_ch
sk_wd:	push	hl		; a word: how long is it?
	ld	b,0
sk_wl:	ld	a,(hl)
	cp	' '+1		; space, 13, 1 and 0 all end it
	jr	c,sk_wl2
	inc	b
	inc	hl
	jr	sk_wl
sk_wl2:	pop	hl
	call	fly		; out of the mouth and up into place
sk_wc:	ld	a,(hl)		; then it stays there
	ld	(de),a
	inc	hl
	inc	de
	djnz	sk_wc
	call	sk_word
	jr	sk_ch
sk_eol:	inc	hl
	push	hl
	ld	hl,(skrow)
	ld	bc,32
	add	hl,bc
	ld	(skrow),hl
	pop	hl
	jr	sk_line
sk_end:	push	hl		; the sentence is complete
	call	showbuf
	pop	hl
	ld	a,(hl)
	or	a
	ret	z		; the last sentence stays up
	inc	hl		; let this one be read, then the next
	push	hl
	ld	b,SENTDLY
	call	sdelay
	pop	hl
	jp	sk_next

sk_word:			; show the sentence so far, then pause (unless hurried)
	push	bc
	push	de
	push	hl
	call	showbuf
	ld	a,(skfast)
	or	a
	jr	nz,sw_1
	ld	b,WORDDLY
	call	sdelay
	jr	z,sw_1
	ld	a,1		; a key: show the rest of this sentence at once
	ld	(skfast),a
sw_1:	pop	hl
	pop	de
	pop	bc
	ret

fly:				; animate word HL (B letters) from the mouth to DE in VBUF
	ld	a,(skfast)
	or	a
	ret	nz
	push	bc
	push	de
	push	hl
	ld	(fword),hl
	ld	a,b
	ld	(flen),a
	ex	de,hl		; target row and column from the VBUF address
	ld	de,VBUF
	or	a
	sbc	hl,de
	ld	a,l
	and	31
	ld	(ftc),a
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	a,h
	ld	(ftr),a
	ld	a,(flen)	; start: centred on the mouth
	srl	a
	ld	b,a
	ld	a,MOUTHCOL
	sub	b
	ld	(fcol),a
	ld	a,MOUTHROW
	ld	(frow),a
fl_1:	call	showbuf		; the view and the words already in place
	call	fl_draw		; the word in flight, straight onto the screen
	ld	b,FLYDLY
	call	sdelay
	jr	nz,fl_hurry
	ld	a,(ftr)		; arrived?
	ld	b,a
	ld	a,(frow)
	sub	b
	jr	z,fl_done
	jr	c,fl_done
	ld	b,a		; B = steps left; move the column 1/B of the way
	ld	a,(fcol)
	ld	c,a
	ld	a,(ftc)
	sub	c
	jr	c,fl_left
	call	fl_div
	add	a,c
	jr	fl_set
fl_left:
	neg
	call	fl_div
	ld	d,a
	ld	a,c
	sub	d
fl_set:	ld	(fcol),a
	ld	hl,frow
	dec	(hl)
	jr	fl_1
fl_hurry:
	ld	a,1
	ld	(skfast),a
fl_done:
	pop	hl
	pop	de
	pop	bc
	ret

fl_div:				; A = A / B
	ld	d,0
fd_d1:	cp	b
	jr	c,fd_d2
	sub	b
	inc	d
	jr	fd_d1
fd_d2:	ld	a,d
	ret

fl_draw:			; draw the flying word at (frow, fcol) in video RAM
	ld	a,(frow)
	ld	l,a
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	de,VIDEO-1
	add	hl,de
	ld	a,(fcol)
	call	addhla
	ld	(hl),20h	; a clear space each side
	inc	hl
	ex	de,hl
	ld	hl,(fword)
	ld	a,(flen)
	ld	c,a
	ld	b,0
	ldir
	ex	de,hl
	ld	(hl),20h
	ret

sdelay:				; wait B x 10 ms; NZ (and stop) if a key is pressed
	call	inkey
	or	a
	ret	nz
	push	bc
	ld	bc,640
sd_1:	dec	bc
	ld	a,b
	or	c
	jr	nz,sd_1
	pop	bc
	djnz	sdelay
	xor	a
	ret

; ===========================================================================
;  Adventure scripting: two-word parser, items, flags, counters and rules
; ===========================================================================
; Each level can carry a script compiled by ZorEdit (see README):
;  items: db count, then per item 6 bytes: x, y, noun, flags (bit 0 = can be
;         carried), name text, description text. Location x = 0 means
;         nowhere (y = 0) or carried (y = 1).
;  rules: db count, then per rule: verb, noun, conditions, actions, followed
;         by 4 bytes (op, a, b, c) per condition and per action.
;         verb 0 = every turn, 255 = on arriving in a cell; noun 0 = any.
;         A condition whose op has bit 7 set is negated.
;  texts: db count, then dw offsets (from the pack start) of each text, 1-based.
; The pack's vocabulary (PK_VOCAB): entries db type (1 verb, 2 noun), id,
; word, 0, ending with type 0. Built-in verbs have ids 1-31, script verbs 32+.
; Bit 6 of a maze cell is set while a script item lies there.

floorcell:			; make cell HL bare (seen) floor, keeping the item bit
	ld	a,(hl)
	and	40h
	or	80h
	ld	(hl),a
	ret

findword:			; HL = typed word, C = type -> A = id, 0 if unknown
	push	hl
	ex	de,hl		; DE = typed word
	ld	hl,(vocabp)
	ld	a,h
	or	l
	jr	z,fw_no
fw_1:	ld	a,(hl)		; entry type
	or	a
	jr	z,fw_no
	cp	c
	inc	hl
	ld	b,(hl)		; its id
	inc	hl
	jr	nz,fw_skip
	push	de
fw_2:	ld	a,(de)
	cp	' '+1		; typed word ended (space or 0)?
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
fw_skip: ld	a,(hl)		; on to the next entry
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
bi_2:	ld	a,(de)		; skip the word
	inc	de
	or	a
	jr	nz,bi_2
	ld	a,(de)
	ld	b,a		; turn flag
	inc	de
	ld	a,(de)		; id
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

; ---- rules ---------------------------------------------------------------------
runverb:			; try the rules for (verbid, nounid)
	xor	a		; -> A: 0 no rule for this verb, 1 none applied,
	ld	(rres),a	;       2 a rule acted, 3 acted and said "continue"
	ld	(contflag),a
	ld	hl,(lrules)
	ld	a,h
	or	l
	ret	z
	ld	b,(hl)
	inc	hl
	ld	a,b
	or	a
	ret	z
rvb_1:	push	bc
	ld	(rptr),hl
	ld	a,(verbid)
	cp	(hl)
	jr	nz,rvb_next
	inc	hl
	ld	a,(hl)		; noun 0 = any
	or	a
	jr	z,rvb_ok
	ld	c,a
	ld	a,(nounid)
	cp	c
	jr	nz,rvb_next
rvb_ok:	ld	a,1
	ld	(rres),a
	ld	hl,(rptr)
	call	tryrule
	jr	nz,rvb_next
	pop	bc
	ld	a,(contflag)
	or	a
	ld	a,2
	ret	z
	inc	a
	ret
rvb_next:
	ld	hl,(rptr)
	call	nextrule
	pop	bc
	djnz	rvb_1
	ld	a,(rres)
	ret

runauto:			; A = 0 every-turn rules, 255 arrival rules: run all that apply
	ld	(autocode),a
	ld	hl,(lrules)
	ld	a,h
	or	l
	ret	z
	ld	b,(hl)
	inc	hl
	ld	a,b
	or	a
	ret	z
ra_1:	push	bc
	ld	(rptr),hl
	ld	a,(autocode)
	cp	(hl)
	call	z,tryrule
	ld	hl,(rptr)
	call	nextrule
	pop	bc
	djnz	ra_1
	ret

nextrule:			; HL = rule -> HL = the next one
	push	hl
	inc	hl
	inc	hl
	ld	a,(hl)
	inc	hl
	add	a,(hl)
	add	a,a
	add	a,a
	add	a,4
	pop	hl
	jp	addhla

tryrule:			; HL = rule: if all conditions hold, do the actions (Z)
	inc	hl
	inc	hl
	ld	b,(hl)		; conditions
	inc	hl
	ld	c,(hl)		; actions
	inc	hl
	ld	a,b
	or	a
	jr	z,tr_act
tr_c:	push	bc
	call	evalcond
	pop	bc
	ret	nz		; a condition failed
	djnz	tr_c
tr_act:	ld	a,c
	or	a
	jr	z,tr_end
	ld	b,c
tr_a:	push	bc
	call	doact
	pop	bc
	djnz	tr_a
tr_end:	ld	a,(idirty)	; items moved: refresh the maze item bits
	or	a
	call	nz,itembits
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
	ld	hl,CONDTAB
	call	ptrtab
	call	jphl
	ld	a,0
	jr	nz,ec_1
	inc	a		; A = 1 if true
ec_1:	ld	b,a
	ld	a,(cop)		; negated?
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
	ld	hl,ACTTAB
	call	ptrtab
	call	jphl
	pop	hl
	ret

jphl:	jp	(hl)

; ---- items ---------------------------------------------------------------------
itemloc:			; A = item -> HL = its location (x, y) in ILOCS
	add	a,a
	ld	hl,ILOCS
	jp	addhla

itemrec:			; A = item -> HL = its 6-byte record in the pack
	ld	c,a
	add	a,a
	add	a,c
	add	a,a
	ld	hl,(litems)
	inc	hl
	jp	addhla

istate:				; A = item -> A: 0 nowhere, 1 carried, 2 here, 3 elsewhere
	call	itemloc
	ld	a,(hl)
	or	a
	jr	nz,is_1
	inc	hl
	ld	a,(hl)
	ret
is_1:	ld	e,a
	inc	hl
	ld	d,(hl)
	ld	a,(px)
	cp	e
	jr	nz,is_2
	ld	a,(py)
	cp	d
	jr	nz,is_2
	ld	a,2
	ret
is_2:	ld	a,3
	ret

iname:				; A = item -> HL = its name text
	call	itemrec
	ld	de,4
	add	hl,de
	ld	a,(hl)
	jp	text

finditem:			; item with noun (nounid) carried (C=1), here (2), either (3)
	ld	a,(nitems)	; -> A = item, or 0FFh
	or	a
	jr	z,fi_no
	ld	b,a
	xor	a
fi_1:	ld	(fitem),a
	push	bc
	call	itemrec
	inc	hl
	inc	hl
	ld	a,(nounid)
	cp	(hl)
	jr	nz,fi_n
	ld	a,(fitem)
	call	istate
	pop	bc
	push	bc
	cp	c
	jr	z,fi_hit
	ld	e,a
	ld	a,c
	cp	3
	jr	nz,fi_n
	ld	a,e
	dec	a
	cp	2
	jr	c,fi_hit
fi_n:	pop	bc
	ld	a,(fitem)
	inc	a
	djnz	fi_1
fi_no:	ld	a,0FFh
	ret
fi_hit:	pop	bc
	ld	a,(fitem)
	ret

firsthere:			; -> A = first item lying here, or 0FFh
	ld	a,(nitems)
	or	a
	jr	z,fh_no
	ld	b,a
	xor	a
fh_1:	ld	(fitem),a
	push	bc
	call	istate
	pop	bc
	cp	2
	ld	a,(fitem)
	ret	z
	inc	a
	djnz	fh_1
fh_no:	ld	a,0FFh
	ret

listitems:			; print the items in state C (1 carried, 2 here)
	ld	a,(nitems)	; as "YOU SEE <NAME>." (here) or "<NAME>" (carried)
	or	a
	ret	z
	ld	b,a
	xor	a
li_1:	ld	(fitem),a
	push	bc
	call	istate
	pop	bc
	cp	c
	jr	nz,li_n
	push	bc
	ld	a,(fitem)
	call	iname
	pop	bc
	push	bc
	ld	a,c
	cp	2
	jr	nz,li_c
	push	hl
	ld	hl,M_YOUSEE
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	call	putc
	jr	li_d
li_c:	call	msg
li_d:	pop	bc
li_n:	ld	a,(fitem)
	inc	a
	djnz	li_1
	ret

setloc:				; item A goes to E = x, D = y
	push	de
	call	itemloc
	pop	de
	ld	(hl),e
	inc	hl
	ld	(hl),d
	ld	a,1
	ld	(idirty),a
	ret

itembits:			; set bit 6 on exactly the cells where items lie
	xor	a
	ld	(idirty),a
	ld	hl,MAZE
	ld	bc,32*24
ib_1:	res	6,(hl)
	inc	hl
	dec	bc
	ld	a,b
	or	c
	jr	nz,ib_1
	ld	a,(nitems)
	or	a
	ret	z
	ld	b,a
	ld	hl,ILOCS
ib_2:	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	a,e
	or	a
	jr	z,ib_3
	push	hl
	push	bc
	call	caddr
	ld	a,h
	or	l
	jr	z,ib_4
	set	6,(hl)
ib_4:	pop	bc
	pop	hl
ib_3:	djnz	ib_2
	ret

text:				; A = text number -> HL = the text ("" for 0)
	or	a
	jr	z,tx_none
	ld	hl,(ltexts)
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

cptr:				; HL = counter (ca)
	ld	a,(ca)
	and	7
	ld	hl,COUNTERS
	jp	addhla

; ---- conditions: Z if true ----------------------------------------------------
CONDTAB: dw	c_false,c_carried,c_here,c_present,c_exists,c_at,c_flag,c_cntge
	dw	c_cntlt,c_facing,c_chance,c_has,c_ahead
c_true:	xor	a
	ret
c_false: or	1
	ret
c_carried:
	ld	a,(ca)
	call	istate
	cp	1
	ret
c_here:	ld	a,(ca)
	call	istate
	cp	2
	ret
c_present:
	ld	a,(ca)
	call	istate
	cp	1
	ret	z
	cp	2
	ret
c_exists:
	ld	a,(ca)
	call	istate
	or	a
	jp	z,c_false
	jp	c_true
c_at:	ld	a,(px)
	ld	b,a
	ld	a,(ca)
	cp	b
	ret	nz
	ld	a,(py)
	ld	b,a
	ld	a,(cb)
	cp	b
	ret
c_flag:	ld	a,(ca)
	call	flagbit
	and	(hl)
	jp	z,c_false
	jp	c_true
c_cntge:
	call	cptr
	ld	a,(cb)
	ld	b,a
	ld	a,(hl)
	cp	b
	jp	c,c_false
	jp	c_true
c_cntlt:
	call	cptr
	ld	a,(cb)
	ld	b,a
	ld	a,(hl)
	cp	b
	jp	c,c_true
	jp	c_false
c_facing:
	call	aheadxy
	call	caddr
	ld	a,h
	or	l
	ld	a,C_WALL
	jr	z,cf_1
	ld	a,(hl)
	and	3Fh
cf_1:	ld	b,a
	ld	a,(ca)
	cp	b
	ret
c_chance:
	call	rnd
	ld	a,(ca)
	ld	b,a
	ld	a,l
	cp	b
	jp	c,c_true
	jp	c_false
c_has:	ld	a,(ca)		; 0 sword, 1 amulet, 2-5 key colour, 6 potion, 7 gold >= b+256c
	or	a
	jr	nz,ch_1
	ld	a,(hassw)
	jr	ch_nz
ch_1:	cp	1
	jr	nz,ch_2
	ld	a,(hasam)
	jr	ch_nz
ch_2:	cp	6
	jr	nc,ch_3
	sub	2
	ld	hl,KEYS
	call	addhla
	ld	a,(hl)
	jr	ch_nz
ch_3:	jr	nz,ch_4
	ld	a,(potions)
	jr	ch_nz
ch_4:	ld	hl,(gold)
	ld	a,(cb)
	ld	e,a
	ld	a,(cc)
	ld	d,a
	or	a
	sbc	hl,de
	jp	c,c_false
	jp	c_true
ch_nz:	or	a
	jp	z,c_false
	jp	c_true
c_ahead:
	call	aheadxy
	ld	a,(ca)
	cp	e
	ret	nz
	ld	a,(cb)
	cp	d
	ret

; ---- actions -------------------------------------------------------------------
ACTTAB:	dw	a_none,a_say,a_speak,a_set,a_clear,a_get,a_drop,a_destroy
	dw	a_put,a_swap,a_cell,a_goto,a_cset,a_cadd,a_csub,a_score
	dw	a_hurt,a_heal,a_give,a_take,a_win,a_die,a_inv,a_look
	dw	a_cont
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
a_get:	ld	de,0100h	; carried
	jr	a_loc
a_drop:	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	jr	a_loc
a_destroy:
	ld	de,0		; nowhere
	jr	a_loc
a_put:	ld	a,(cb)
	ld	e,a
	ld	a,(cc)
	ld	d,a
a_loc:	ld	a,(ca)
	jp	setloc
a_swap:	ld	a,(ca)
	call	itemloc
	push	hl
	ld	a,(cb)
	call	itemloc
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
a_cell:	ld	a,(ca)
	ld	e,a
	ld	a,(cb)
	ld	d,a
	call	caddr
	ld	a,h
	or	l
	ret	z
	ld	a,(hl)
	and	0C0h
	ld	b,a
	ld	a,(cc)
	or	b
	ld	(hl),a
	ret
a_goto:	ld	a,(ca)
	ld	(px),a
	ld	a,(cb)
	ld	(py),a
	call	herecode
	set	7,(hl)
	ret
a_cset:	call	cptr
	ld	a,(cb)
	ld	(hl),a
	ret
a_cadd:	call	cptr
	ld	a,(cb)
	add	a,(hl)
	jr	nc,ac_1
	ld	a,255
ac_1:	ld	(hl),a
	ret
a_csub:	call	cptr
	ld	a,(cb)
	ld	b,a
	ld	a,(hl)
	sub	b
	jr	nc,ac_1
	xor	a
	jr	ac_1
a_score:
	ld	hl,(points)
	ld	a,(ca)
	ld	e,a
	ld	a,(cb)
	ld	d,a
	add	hl,de
	ld	(points),hl
	ret
a_hurt:	ld	a,(ca)
	ld	b,a
	jp	hurt
a_heal:	ld	a,(maxhp)
	ld	c,a
	ld	a,(ca)
	call	orelse
	ld	b,a
	call	heal_by
	ld	hl,M_HPIS
	call	msg
	ld	a,(hp)
	jp	putdeca
a_give:	ld	a,(ca)		; 0 sword, 1 amulet, 2-5 key, 6 potion, 7 gold b+256c
	or	a
	jr	nz,ag_1
	inc	a
	ld	(hassw),a
	ret
ag_1:	cp	1
	jr	nz,ag_2
	ld	(hasam),a
	ret
ag_2:	cp	6
	jr	nc,ag_3
	sub	2
	ld	hl,KEYS
	call	addhla
	inc	(hl)
	ret
ag_3:	jr	nz,ag_4
	ld	a,(potions)
	cp	16
	ret	nc
	ld	hl,PHEAL
	call	addhla
	ld	a,(heal)
	ld	(hl),a
	ld	hl,potions
	inc	(hl)
	ret
ag_4:	ld	hl,(gold)
	ld	a,(cb)
	ld	e,a
	ld	a,(cc)
	ld	d,a
	add	hl,de
	ld	(gold),hl
	ret
a_take:	ld	a,(ca)		; the reverse of a_give
	or	a
	jr	nz,at2_1
	ld	(hassw),a
	ret
at2_1:	cp	1
	jr	nz,at2_2
	xor	a
	ld	(hasam),a
	ret
at2_2:	cp	6
	jr	nc,at2_3
	sub	2
	ld	hl,KEYS
	call	addhla
at2_dec: ld	a,(hl)
	or	a
	ret	z
	dec	(hl)
	ret
at2_3:	jr	nz,at2_4
	ld	hl,potions
	jr	at2_dec
at2_4:	ld	hl,(gold)
	ld	a,(cb)
	ld	e,a
	ld	a,(cc)
	ld	d,a
	or	a
	sbc	hl,de
	jr	nc,at2_5
	ld	hl,0
at2_5:	ld	(gold),hl
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

; ---- built-in commands for script items ------------------------------------
take_item:			; take item A (if it can be carried)
	ld	(curitem),a
	call	itemrec
	inc	hl
	inc	hl
	inc	hl
	bit	0,(hl)
	jr	z,ti_no
	ld	de,0100h
	ld	a,(curitem)
	call	setloc
	call	itembits
	ld	a,(curitem)
	call	iname
	push	hl
	ld	hl,M_TAKE
	call	msg
	pop	hl
	call	msgc
	ld	a,'.'
	jp	putc
ti_no:	ld	hl,M_CANTCARRY
	jp	msg

act_drop:
	ld	a,(nounid)
	or	a
	jr	nz,dp_1
	ld	hl,M_DROPWHAT
	jp	msg
dp_1:	ld	c,1
	call	finditem
	cp	0FFh
	jr	nz,dp_2
	ld	hl,M_NOTHAVE
	jp	msg
dp_2:	push	af
	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	pop	af
	call	setloc
	call	itembits
	ld	hl,M_DROPPED
	jp	msg

look_item:			; LOOK/EXAMINE <noun>: Z if it was a script item
	ld	a,(nounid)
	or	a
	jr	z,lki_no
	ld	c,3
	call	finditem
	cp	0FFh
	jr	z,lki_no
	call	itemrec
	ld	de,5
	add	hl,de
	ld	a,(hl)
	or	a
	ld	hl,M_NOSPECIAL
	jr	z,lki_1
	call	text
lki_1:	call	msg
	xor	a
	ret
lki_no:	or	1
	ret

arrive:				; arriving in a cell: built-in events, items, arrival rules
	call	arrive0
	ld	c,2
	call	listitems
	ld	a,255
	jp	runauto

gturn:				; end of a turn: guardians, then every-turn rules
	call	gguard
	xor	a
	jp	runauto

act_help:
	ld	hl,M_HELP
	jp	msg

act_score:
	ld	hl,M_SCORE
	call	msg
	call	score
	call	putdec
	ld	hl,M_MOVES
	call	msgc
	ld	hl,(moves)
	jp	putdec

score:				; HL = gold + 5*hp (+100 with amulet)
	ld	a,(hp)
	ld	l,a
	ld	h,0
	ld	d,h
	ld	e,l
	add	hl,hl
	add	hl,hl
	add	hl,de
	ld	de,(gold)
	add	hl,de
	ld	de,(points)	; + points from the script
	add	hl,de
	ld	a,(amulets)
	ld	de,100
sc_1:	or	a
	ret	z
	add	hl,de
	dec	a
	jr	sc_1

act_quit:
	ld	hl,M_QUIT
	call	msg
	call	waitkey
	and	0DFh		; upper case
	cp	'Y'
	jp	z,exitdos
	ld	hl,M_OK
	jp	msg

; ---- end of every turn: the guardian lashes out at a neighbour ------------
gguard:	ld	hl,(moves)
	inc	hl
	ld	(moves),hl
	call	herecode
	ld	de,1
	call	gt_chk
	ld	de,-1
	call	gt_chk
	ld	de,32
	call	gt_chk
	ld	de,-32
	call	gt_chk
	ret
gt_chk:	push	hl
	add	hl,de
	ld	a,(hl)
	and	3Fh
	cp	C_GUARD
	jr	z,gt_hit
	pop	hl
	ret
gt_hit:	call	getobj		; this guardian's attack chance and damage
	pop	hl
	pop	de		; adjacent: drop return address, attack
	call	rnd
	ld	a,(gch)
	ld	c,a
	ld	a,(ix+4)
	call	orelse
	ld	c,a
	ld	a,l
	cp	c
	ret	nc
	ld	a,(ix+3)	; damage 1 .. max (default 3)
	ld	c,3
	call	orelse
	ld	b,a
	ld	a,h
	and	7Fh
	call	modb
	inc	a
gt_1:	ld	b,a
	ld	a,(hp)
	sub	b
	jr	nc,gt_2
	xor	a
gt_2:	ld	(hp),a
	ld	hl,M_GORE
	call	msg
	ld	a,b
	call	putdeca
	ld	hl,M_HP
	call	msgc
	ld	a,(hp)
	or	a
	ret	nz
; ---- death and victory -----------------------------------------------------
death:	call	status
	ld	hl,M_DEAD
	call	msg
	call	waitenter
	jp	gotitle

win:	call	render		; show the ladder under the player
	call	drawmap
	call	status
	ld	hl,amulets
	inc	(hl)
	ld	a,(level)
	inc	a
	ld	b,a
	ld	hl,(packp)
	ld	de,PK_NLEV
	add	hl,de
	ld	a,(hl)
	cp	b
	jr	z,win_end
	ld	hl,M_LVDONE	; more levels to go
	call	msg
	call	waitenter
	ld	hl,level
	inc	(hl)
	ld	sp,STACK
	call	loadlevel
	jp	mainlp
win_end:
	ld	a,PK_VICT
	call	packw
	call	msg
	ld	hl,M_WIN
	call	msg
	ld	hl,M_SCORE
	call	msg
	call	score
	call	putdec
	ld	hl,M_MOVES
	call	msgc
	ld	hl,(moves)
	call	putdec
	ld	hl,M_ENTER
	call	msg
	call	waitenter
	jp	gotitle

; ===========================================================================
;  Tables
; ===========================================================================
DXT:	db	0,1,0,-1	; dx for N E S W
DYT:	db	-1,0,1,0	; dy (getvec reads DXT+4)
ARROWS:	db	5Bh,5Eh,5Ch,5Dh	; Model I up/right/down/left arrow glyphs
DIRL:	db	"NESW"
DIRNAME: dw	D_N,D_E,D_S,D_W
D_N:	db	"NORTH",0
D_E:	db	"EAST",0
D_S:	db	"SOUTH",0
D_W:	db	"WEST",0

; map characters, two per cell code
MAPCH:	db	88h,80h		; floor (faint dot)
	db	0BFh,0BFh	; wall
	db	"##"		; door
	db	"H",80h		; ladder
	db	"G",80h		; guardian
	db	"K",80h		; key
	db	"S",80h		; sword
	db	"P",80h		; potion
	db	"$",80h		; gold
	db	"A",80h		; amulet
	db	"?",80h		; sign
	db	"*",80h		; trap (once seen)
	db	"@",80h		; teleporter
	db	"W",80h		; fountain
	db	"=",80h		; pressure plate
	db	0BFh,0BFh	; secret wall (looks like wall)
	db	"&",80h		; hermit
	db	"&",80h		; merchant
	db	"&",80h		; ghost
	db	"+",80h		; a script item

DEFOBJ:	db	0,0,0,0,0,0,0,0	; parameters of a cell without a record
LKSPACE: dw	M_LSIGN,M_LTRAP,M_LTELE,M_LFOUNT,M_LPLATE,M_LWALL


; vocabulary: word, 0, takes-a-turn flag, handler
; vocabulary: word, 0, takes-a-turn flag, verb id, handler
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
	db	"O",0,1,8
	dw	act_open
	db	"OPEN",0,1,8
	dw	act_open
	db	"UNLOCK",0,1,8
	dw	act_open
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
	db	"U",0,1,9
	dw	act_use
	db	"USE",0,1,9
	dw	act_use
	db	"A",0,1,10
	dw	act_attack
	db	"ATTACK",0,1,10
	dw	act_attack
	db	"FIGHT",0,1,10
	dw	act_attack
	db	"KILL",0,1,10
	dw	act_attack
	db	"HIT",0,1,10
	dw	act_attack
	db	"D",0,1,11
	dw	act_drink
	db	"DRINK",0,1,11
	dw	act_drink
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
	db	"F B L R TURN  TAKE OPEN USE",13
	db	"ATTACK DRINK TALK LOOK INV",13
	db	"SCORE QUIT DROP. TRY ANYTHING!",0
M_LEVEL:	db	"LEVEL ",0
M_LVDONE:	db	"YOU ESCAPE WITH THE AMULET!",13
	db	"LEVEL COMPLETE. PRESS ENTER",13
	db	"TO DESCEND FURTHER.",0
M_TIMES:	db	" X ",0
M_DISK:	db	"CANNOT LOAD THE LEVEL FILE",0
M_NOTFND: db	"LEVEL FILE NOT FOUND ON ANY DRIVE",0
M_ERRNO:	db	" - TRSDOS ERROR ",0
M_BIG:	db	"LEVEL FILE TOO BIG FOR MEMORY",0
M_BADPK:	db	"NOT A VAULTS OF ZOR LEVEL FILE",0
M_ANYKEY:	db	"PRESS ANY KEY TO RETURN TO TRSDOS.",0
DEFNAME:	db	"ZOR/LEV",13
PKSIG:	db	"ZORPACK",4
M_ECHO:	db	"> ",0
M_UNK:	db	"I DON'T UNDERSTAND THAT.",0
M_CANT:	db	"YOU CAN'T DO THAT... YET.",0
M_NOSEE: db	"I DON'T SEE IT HERE.",0
M_CANTCARRY: db	"YOU CAN'T CARRY THAT.",0
M_DROPWHAT: db	"DROP WHAT?",0
M_NOTHAVE: db	"YOU'RE NOT CARRYING IT.",0
M_DROPPED: db	"DROPPED.",0
M_NOSPECIAL: db	"YOU SEE NOTHING SPECIAL.",0
M_EMPTY: db	0
M_BUMP:	db	"YOU BUMP INTO SOLID STONE.",0
M_DOOR:	db	"THE DOOR IS LOCKED.",0
M_BARS:	db	"THE GUARDIAN BARS THE WAY!",0
M_YOUSEE: db	"YOU SEE ",0
M_AHEAD: db	"AHEAD LIES ",0
M_TAKE:	db	"YOU TAKE ",0
M_GOTAM: db	"NOW BACK TO THE LADDER!",0
M_GOLD1: db	"YOU GATHER ",0
M_GOLD2: db	" GOLD PIECES.",0
M_NOTAKE: db	"THERE IS NOTHING HERE.",0
M_LADDER: db	"THE LADDER LEADS UP TO",13
	db	"DAYLIGHT - BUT NOT WITHOUT",13
	db	"THE AMULET!",0
M_OPENED: db	"THE KEY TURNS. THE DOOR",13
	db	"GROANS OPEN.",0
M_NEED:	db	"LOCKED. YOU NEED ",0
M_DNEED: db	"IT TAKES ",0
M_SIGNHD: db	"A SIGN READS:",0
M_SIGN0: db	"THE WORDS ARE WORN AWAY.",0
M_TRAP:	db	"CLICK! A TRAP SPRINGS!",0
M_LOSE:	db	"YOU LOSE ",0
M_HPD:	db	" HP.",0
M_TELE:	db	"THE FLOOR GLOWS AND THE VAULT",13
	db	"SPINS AROUND YOU...",0
M_FOUNT: db	"YOU DRINK FROM THE FOUNTAIN.",0
M_HPIS:	db	"HP NOW ",0
M_DRY:	db	"THE FOUNTAIN RUNS DRY.",0
M_PLATE: db	"CLICK! STONE GRINDS NEARBY.",0
M_SECRET: db	"THE WALL WAS AN ILLUSION!",0
M_SAYS:	db	" SAYS:",0
M_NPC0:	db	"HELLO, TRAVELLER.",0
M_RECV:	db	"YOU RECEIVE ",0
M_PAY:	db	"YOU PAY ",0
M_GOLDD: db	" GOLD.",0
M_NEEDG: db	"YOU NEED ",0
M_VANISH: db	"AND WITH THAT, THEY ARE GONE.",0
M_NOONE: db	"THERE IS NO ONE TO TALK TO.",0
M_ASTAND: db	"AHEAD STANDS ",0
M_LSIGN: db	"A SIGN STANDS AHEAD.",0
M_LTRAP: db	"A TRAP LIES AHEAD!",0
M_LTELE: db	"A GLOWING CIRCLE IS AHEAD.",0
M_LFOUNT: db	"A FOUNTAIN BUBBLES AHEAD.",0
M_LPLATE: db	"A STONE PLATE IS AHEAD.",0
M_NOOPEN: db	"THERE IS NOTHING TO OPEN.",0
M_DRINK: db	"YOU FEEL STRONGER. HP ",0
M_NOPOT: db	"YOU HAVE NO POTION.",0
M_HIT:	db	"YOUR SWORD BITES DEEP!",0
M_KILL:	db	"THE GUARDIAN CRASHES DOWN,",13
	db	"DEAD!",0
M_MISS:	db	"THE GUARDIAN DODGES.",0
M_FIST:	db	"YOUR FISTS DO NOTHING!",13
	db	"YOU NEED A WEAPON.",0
M_AIR:	db	"YOU SWING AT EMPTY AIR.",0
M_FACE:	db	"YOU FACE ",0
M_LWALL: db	"A WALL OF COLD, DAMP STONE.",0
M_LDOOR: db	"A STOUT IRON-BOUND DOOR.",0
M_LGRD:	db	"THE HORNED GUARDIAN GLARES",13
	db	"AT YOU AND SNORTS.",0
M_LLAD:	db	"THE LADDER UP TO DAYLIGHT.",0
M_LPASS: db	"THE PASSAGE LEADS ON.",0
M_CARRY: db	"YOU CARRY:",0
M_POTS:	db	" POTION(S)",0
M_GP:	db	" GOLD PIECES",0
M_SCORE: db	"SCORE ",0
M_MOVES: db	"  MOVES ",0
M_QUIT:	db	"QUIT TO TRSDOS? (Y/N)",0
M_OK:	db	"VERY WELL.",0
M_GORE:	db	"THE GUARDIAN GORES YOU! -",0
M_HP:	db	" HP",0
M_DEAD:	db	"YOU HAVE DIED IN THE DARK.",13
	db	"PRESS ENTER.",0
M_WIN:	db	"*** YOU HAVE WON ***",0
M_ENTER: db	"PRESS ENTER.",0
S_HP:	db	" HP:",0
S_GOLD:	db	" $:",0
S_POT:	db	" P:",0

	include	gfx.inc

; ===========================================================================
;  Variables
; ===========================================================================
seed:	ds	2
px:	ds	1
py:	ds	1
dir:	ds	1
fx:	ds	1
fy:	ds	1
lfx:	ds	1
lfy:	ds	1
cx:	ds	1
cy:	ds	1
mx:	ds	1
my:	ds	1
front:	ds	1
kmax:	ds	1
segi:	ds	1
hp:	ds	1
ghp:	ds	1
KEYS:	ds	4		; keys held, per colour
nobj:	ds	1		; records in OBJS
OBJS:	ds	MAXOBJ*8
PHEAL:	ds	16		; strength of each potion carried
hasobj:	ds	1
teleflag: ds	1
npccell: ds	2
sp1:	ds	2
sp2:	ds	2
sptxt:	ds	2
skfast:	ds	1		; 1 = finish the sentence at once
skrow:	ds	2
fword:	ds	2		; the word in flight
flen:	ds	1
frow:	ds	1
fcol:	ds	1
ftr:	ds	1
ftc:	ds	1
keepview: ds	1
verbid:	ds	1		; the command being obeyed
nounid:	ds	1
vhandler: ds	2
rres:	ds	1
contflag: ds	1
idirty:	ds	1
autocode: ds	1
rptr:	ds	2
cop:	ds	1		; condition/action being evaluated: op, a, b, c
ca:	ds	1
cb:	ds	1
cc:	ds	1
fitem:	ds	1
curitem: ds	1
nitems:	ds	1
ILOCS: ds	MAXITEMS*2
FLAGS:	ds	4		; 32 script flags (whole game)
COUNTERS: ds	8		; 8 script counters
points:	ds	2
vocabp:	ds	2
litems:	ds	2
lrules:	ds	2
ltexts:	ds	2
ghpmax:	ds	1
gaddr:	ds	2
level:	ds	1
amulets:	ds	1
starthp:	ds	1		; 7 bytes copied from the pack header
maxhp:	ds	1
heal:	ds	1
hitch:	ds	1
gch:	ds	1
goldmin:	ds	1
goldrng:	ds	1
lvname:	ds	2
lvintro:	ds	2
tailp:	ds	2
packp:	ds	2
packbuf:	ds	2
dest:	ds	2
errcode:	ds	1
noshow:	ds	1		; 1 = render into VBUF only
kqn:	ds	1		; keys waiting in KQ
KQ:	ds	16
mvback:	ds	1
tdir:	ds	1
tshift:	ds	1
toff:	ds	2
gSEGL:	ds	2		; tables for the phase being drawn (copied from PHASETAB)
gSEGR:	ds	2
gSEGF:	ds	2
gF5:	ds	2
gSPR:	ds	2
OLDBUF:	ds	32*16
FCB:	ds	50
SECBUF:	ds	256
hassw:	ds	1
hasam:	ds	1
potions: ds	1
gold:	ds	2
moves:	ds	2
omode:	ds	1
vptr:	ds	2
trow:	ds	1
tcol:	ds	1
inlen:	ds	1
tflag:	ds	1
wordp:	ds	2
nounp:	ds	2
INBUF:	ds	32
VA:	ds	30
VBUF:	ds	32*16
MAZE:	ds	32*24
	ds	256
STACK:

; ---------------------------------------------------------------------------
;  Built-in level pack. ZorEdit replaces everything from here on when it
;  exports a game .CMD; this empty placeholder (no levels) makes the game
;  load ZOR/LEV from disk instead.
; ---------------------------------------------------------------------------
BUILTIN:
	db	"ZORPACK",4
	dw	46
	db	0
	ds	35

	end	entry
